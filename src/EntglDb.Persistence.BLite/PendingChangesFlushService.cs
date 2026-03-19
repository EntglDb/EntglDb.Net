using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BLite.Core.Query;
using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Persistence.BLite.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Persistence.BLite;

/// <summary>
/// Flushes pending local changes (recorded by IPendingChangesService) into the oplog.
/// Reads document content from the application's IDocumentStore, writes oplog entries and
/// DocumentMetadata into the shared EntglDbMetaContext (BLite internal DB).
///
/// Usable for both BLite-backed and EF Core-backed document stores: the meta store is always
/// EntglDbMetaContext regardless of which persistence provider the application uses.
/// </summary>
public sealed class PendingChangesFlushService : IPendingChangesFlushService
{
    private readonly IPendingChangesService _pending;
    private readonly IDocumentStore _documentStore;
    private readonly EntglDbMetaContext _meta;
    private readonly IPeerNodeConfigurationProvider _configProvider;
    private readonly IVectorClockService _vectorClock;
    private readonly ILogger<PendingChangesFlushService> _logger;

    // Protects hash-chain integrity: only one flush at a time.
    private readonly SemaphoreSlim _flushLock = new SemaphoreSlim(1, 1);

    // NodeId is fixed at runtime — cached on first use.
    private readonly Lazy<string> _nodeId;

    public PendingChangesFlushService(
        IPendingChangesService pendingChangesService,
        IDocumentStore documentStore,
        EntglDbMetaContext meta,
        IPeerNodeConfigurationProvider configProvider,
        IVectorClockService vectorClock,
        ILogger<PendingChangesFlushService>? logger = null)
    {
        _pending = pendingChangesService ?? throw new ArgumentNullException(nameof(pendingChangesService));
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        _meta = meta ?? throw new ArgumentNullException(nameof(meta));
        _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
        _vectorClock = vectorClock ?? throw new ArgumentNullException(nameof(vectorClock));
        _logger = logger ?? NullLogger<PendingChangesFlushService>.Instance;

        _nodeId = new Lazy<string>(() => _configProvider.GetConfiguration().GetAwaiter().GetResult().NodeId);
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _flushLock.WaitAsync(cancellationToken);
        try
        {
            var changes = await _pending.PopAllAsync(cancellationToken);
            if (changes.Count == 0) return;

            _logger.LogDebug("Flushing {Count} pending changes to oplog", changes.Count);

            foreach (var change in changes)
                await ProcessOneChangeAsync(change, cancellationToken);
        }
        finally
        {
            _flushLock.Release();
        }
    }

    private async Task ProcessOneChangeAsync(PendingChange change, CancellationToken cancellationToken)
    {
        var nodeId = _nodeId.Value;

        // Retrieve the last oplog entry for this node to build the previousHash chain.
        var lastEntry = await _meta.OplogEntries
            .AsQueryable()
            .Where(e => e.TimestampNodeId == nodeId)
            .OrderByDescending(e => e.TimestampPhysicalTime)
            .ThenByDescending(e => e.TimestampLogicalCounter)
            .FirstOrDefaultAsync(cancellationToken);

        var previousHash = lastEntry?.Hash ?? string.Empty;

        // For Put: read current content from the application document store.
        var operationType = change.OperationType;
        JsonElement? content = null;

        if (operationType == OperationType.Put)
        {
            var doc = await _documentStore.GetDocumentAsync(change.Collection, change.Key, cancellationToken);
            if (doc == null)
            {
                // Document was deleted after the pending change was recorded — promote to Delete.
                operationType = OperationType.Delete;
            }
            else
            {
                content = doc.Content;
            }
        }

        // Dedup: if DocumentMetadata already has the same ContentHash the write came from sync — skip.
        var incomingHash = operationType == OperationType.Delete
            ? ""
            : EntityMappers.ComputeContentHash(content);

        var metadataId = $"{change.Collection}/{change.Key}";
        var existingMetadata = await _meta.DocumentMetadatas.FindByIdAsync(metadataId, cancellationToken);

        if (existingMetadata != null && existingMetadata.ContentHash == incomingHash)
        {
            _logger.LogDebug(
                "Skipping pending change {Collection}/{Key}: same ContentHash already in metadata",
                change.Collection, change.Key);
            return;
        }

        // Ensure monotonic HLC: if the stored timestamp is not strictly greater than the last
        // oplog entry for this node, advance the logical counter.
        HlcTimestamp timestamp;
        if (lastEntry != null &&
            (change.Timestamp.PhysicalTime < lastEntry.TimestampPhysicalTime ||
             (change.Timestamp.PhysicalTime == lastEntry.TimestampPhysicalTime &&
              change.Timestamp.LogicalCounter <= lastEntry.TimestampLogicalCounter)))
        {
            timestamp = new HlcTimestamp(
                lastEntry.TimestampPhysicalTime,
                lastEntry.TimestampLogicalCounter + 1,
                nodeId);
        }
        else
        {
            timestamp = change.Timestamp;
        }

        var oplogEntry = new OplogEntry(
            change.Collection,
            change.Key,
            operationType,
            content,
            timestamp,
            previousHash);

        // Persist oplog entry.
        await _meta.OplogEntries.InsertAsync(oplogEntry.ToEntity(), cancellationToken);

        // Upsert DocumentMetadata.
        if (existingMetadata != null)
        {
            existingMetadata.HlcPhysicalTime = timestamp.PhysicalTime;
            existingMetadata.HlcLogicalCounter = timestamp.LogicalCounter;
            existingMetadata.HlcNodeId = timestamp.NodeId;
            existingMetadata.IsDeleted = operationType == OperationType.Delete;
            existingMetadata.ContentHash = incomingHash;
            await _meta.DocumentMetadatas.UpdateAsync(existingMetadata, cancellationToken);
        }
        else
        {
            await _meta.DocumentMetadatas.InsertAsync(
                EntityMappers.CreateDocumentMetadata(
                    change.Collection, change.Key, timestamp,
                    operationType == OperationType.Delete, content),
                cancellationToken);
        }

        await _meta.SaveChangesAsync(cancellationToken);

        // Notify VectorClock so the next sync cycle sees this local change.
        _vectorClock.Update(oplogEntry);

        _logger.LogDebug(
            "Flushed pending change: {Operation} {Collection}/{Key} at {Timestamp} (hash: {Hash})",
            operationType, change.Collection, change.Key, timestamp, oplogEntry.Hash);
    }
}
