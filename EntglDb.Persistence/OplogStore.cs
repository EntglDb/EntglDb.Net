using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Core.Network;
using EntglDb.Core.Sync;

namespace EntglDb.Persistence.Sqlite;

public abstract class OplogStore : IOplogStore
{
    protected readonly IDocumentStore _documentStore;
    protected readonly IConflictResolver _conflictResolver;
    protected readonly ISnapshotMetadataStore? _snapshotMetadataStore;
    protected readonly IVectorClockService _vectorClock;

    public event EventHandler<ChangesAppliedEventArgs> ChangesApplied;

    public virtual void OnChangesApplied(IEnumerable<OplogEntry> appliedEntries)
    {
        ChangesApplied?.Invoke(this, new ChangesAppliedEventArgs(appliedEntries));
    }

    public OplogStore(
        IDocumentStore documentStore,
        IConflictResolver conflictResolver,
        IVectorClockService vectorClockService,
        ISnapshotMetadataStore? snapshotMetadataStore = null)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        _conflictResolver = conflictResolver ?? throw new ArgumentNullException(nameof(conflictResolver));
        _vectorClock = vectorClockService ?? throw new ArgumentNullException(nameof(vectorClockService));
        _snapshotMetadataStore = snapshotMetadataStore;
        InitializeVectorClock();
    }

    /// <summary>
    /// Initializes the VectorClockService with existing oplog/snapshot data.
    /// Called once at construction time.
    /// </summary>
    protected abstract void InitializeVectorClock();

    /// <summary>
    /// Asynchronously inserts an operation log entry into the underlying data store.
    /// </summary>
    /// <remarks>Implementations should ensure that the entry is persisted reliably. If the operation is
    /// cancelled, the entry may not be inserted.</remarks>
    /// <param name="entry">The operation log entry to insert. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the insert operation.</param>
    /// <returns>A task that represents the asynchronous insert operation.</returns>
    protected abstract Task InsertOplogEntryAsync(OplogEntry entry, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public async Task AppendOplogEntryAsync(OplogEntry entry, CancellationToken cancellationToken = default)
    {
        await InsertOplogEntryAsync(entry, cancellationToken);
        _vectorClock.Update(entry);
    }

    /// <inheritdoc />
    public async virtual Task ApplyBatchAsync(IEnumerable<OplogEntry> oplogEntries, CancellationToken cancellationToken = default)
    {
        var documentKeys = oplogEntries.Select(e => (e.Collection, e.Key)).Distinct().ToList();
        var documentsToFetch = await _documentStore.GetDocumentsAsync(documentKeys, cancellationToken);

        var orderdedEntriesPerCollectionKey = oplogEntries
            .GroupBy(e => (e.Collection, e.Key))
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Timestamp.PhysicalTime)
                                             .ThenBy(e => e.Timestamp.LogicalCounter)
                                             .ToList());

        foreach (var entry in orderdedEntriesPerCollectionKey)
        {
            var document = documentsToFetch.FirstOrDefault(d => d.Collection == entry.Key.Collection && d.Key == entry.Key.Key);

            if (entry.Value.Any(v => v.Operation == OperationType.Delete))
            {
                if (document != null)
                {
                    await _documentStore.DeleteDocumentAsync(entry.Key.Collection, entry.Key.Key, cancellationToken);
                }
                continue;
            }

            var documentHash = document != null ? document.GetHashCode().ToString() : null;

            foreach (var oplogEntry in entry.Value)
            {
                if (document == null && (oplogEntry.Operation == OperationType.Put) && oplogEntry.Payload != null)
                {
                    document = new Document(oplogEntry.Collection, oplogEntry.Key, oplogEntry.Payload!.Value, oplogEntry.Timestamp, false);
                }
                else
                {
                    document?.Merge(oplogEntry, _conflictResolver);
                }
            }

            if (document?.GetHashCode().ToString() != documentHash)
            {
                await _documentStore.PutDocumentAsync(document!, cancellationToken);
            }
        }

        //insert all oplog entries after processing documents to ensure oplog reflects the actual state of documents
        await MergeAsync(oplogEntries, cancellationToken);

        _vectorClock.Invalidate();
        InitializeVectorClock();
        OnChangesApplied(oplogEntries);
    }

    /// <inheritdoc />
    public abstract Task<IEnumerable<OplogEntry>> GetChainRangeAsync(string startHash, string endHash, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<OplogEntry?> GetEntryByHashAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves the most recent hash value associated with the specified node.
    /// </summary>
    /// <param name="nodeId">The unique identifier of the node for which to query the last hash. Cannot be null or empty.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the last hash value for the node, or
    /// null if no hash is available.</returns>
    protected abstract Task<string?> QueryLastHashForNodeAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously queries the oplog for the most recent timestamp associated with the specified hash.
    /// </summary>
    /// <remarks>This method is intended to be implemented by derived classes to provide access to the oplog.
    /// The returned timestamps can be used to track the last occurrence of a hash in the oplog for synchronization or
    /// auditing purposes.</remarks>
    /// <param name="hash">The hash value to search for in the oplog. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a tuple with the wall clock
    /// timestamp and logical timestamp if the hash is found; otherwise, null.</returns>
    protected abstract Task<(long Wall, int Logic)?> QueryLastHashTimestampFromOplogAsync(string hash, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public async Task<string?> GetLastEntryHashAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        // Try cache first
        var cachedHash = _vectorClock.GetLastHash(nodeId);
        if (cachedHash != null) return cachedHash;

        // Cache miss - query database (Oplog first)
        var hash = await QueryLastHashForNodeAsync(nodeId, cancellationToken);

        // FALLBACK: If not in oplog, check SnapshotMetadata (important after prune!)
        if (hash == null && _snapshotMetadataStore != null)
        {
            hash = await _snapshotMetadataStore.GetSnapshotHashAsync(nodeId, cancellationToken);
            
            if (hash != null)
            {
                var snapshotMeta = await _snapshotMetadataStore.GetSnapshotMetadataAsync(nodeId, cancellationToken);
                if (snapshotMeta != null)
                {
                    _vectorClock.UpdateNode(nodeId,
                        new HlcTimestamp(snapshotMeta.TimestampPhysicalTime, snapshotMeta.TimestampLogicalCounter, nodeId),
                        hash);
                }
                return hash;
            }
        }

        // Update cache if found in oplog
        if (hash != null)
        {
            var row = await QueryLastHashTimestampFromOplogAsync(hash, cancellationToken);
            if (row.HasValue)
            {
                _vectorClock.UpdateNode(nodeId,
                    new HlcTimestamp(row.Value.Wall, row.Value.Logic, nodeId),
                    hash);
            }
        }

        return hash;
    }

    /// <inheritdoc />
    public Task<HlcTimestamp> GetLatestTimestampAsync(CancellationToken cancellationToken = default)
    {
        return _vectorClock.GetLatestTimestampAsync(cancellationToken);
    }

    /// <inheritdoc />
    public abstract Task<IEnumerable<OplogEntry>> GetOplogAfterAsync(HlcTimestamp timestamp, IEnumerable<string>? collections = null, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<IEnumerable<OplogEntry>> GetOplogForNodeAfterAsync(string nodeId, HlcTimestamp since, IEnumerable<string>? collections = null, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public Task<VectorClock> GetVectorClockAsync(CancellationToken cancellationToken = default)
    {
        return _vectorClock.GetVectorClockAsync(cancellationToken);
    }

    /// <inheritdoc />
    public abstract Task PruneOplogAsync(HlcTimestamp cutoff, CancellationToken cancellationToken = default);

    public abstract Task DropAsync(CancellationToken cancellationToken = default);

    public abstract Task<IEnumerable<OplogEntry>> ExportAsync(CancellationToken cancellationToken = default);

    public abstract Task ImportAsync(IEnumerable<OplogEntry> items, CancellationToken cancellationToken = default);

    public abstract Task MergeAsync(IEnumerable<OplogEntry> items, CancellationToken cancellationToken = default);
}

