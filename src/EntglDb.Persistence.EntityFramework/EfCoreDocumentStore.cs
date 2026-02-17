using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Persistence.EntityFramework.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Persistence.EntityFramework;

/// <summary>
/// Abstract base class for EF Core-based document stores.
/// Handles Oplog and DocumentMetadata creation internally - subclasses only implement entity mapping.
/// </summary>
/// <typeparam name="TDbContext">The EF Core DbContext type.</typeparam>
public abstract class EfCoreDocumentStore<TDbContext> : IDocumentStore, IDisposable
    where TDbContext : DbContext
{
    protected readonly TDbContext _context;
    protected readonly IPeerNodeConfigurationProvider _configProvider;
    protected readonly IConflictResolver _conflictResolver;
    protected readonly ILogger _logger;

    /// <summary>
    /// Thread-local flag to suppress Oplog creation during remote sync operations.
    /// When true, write to DB only (Oplog entry already exists from remote).
    /// </summary>
    private static readonly AsyncLocal<bool> _isRemoteSync = new AsyncLocal<bool>();

    // HLC state for generating timestamps for local changes
    private long _lastPhysicalTime;
    private int _logicalCounter;
    private readonly object _clockLock = new object();

    protected EfCoreDocumentStore(
        TDbContext context,
        IPeerNodeConfigurationProvider configProvider,
        IConflictResolver? conflictResolver = null,
        ILogger? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
        _conflictResolver = conflictResolver ?? new LastWriteWinsConflictResolver();
        _logger = logger ?? NullLogger.Instance;

        _lastPhysicalTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _logicalCounter = 0;
    }

    #region Abstract Methods - Implemented by subclass

    /// <summary>
    /// Applies JSON content to an entity in the DbContext (insert or update).
    /// </summary>
    protected abstract Task ApplyContentToEntityAsync(
        string collection, string key, JsonElement content, CancellationToken cancellationToken);

    /// <summary>
    /// Reads an entity from the DbContext and returns it as JsonElement.
    /// </summary>
    protected abstract Task<JsonElement?> GetEntityAsJsonAsync(
        string collection, string key, CancellationToken cancellationToken);

    /// <summary>
    /// Removes an entity from the DbContext.
    /// </summary>
    protected abstract Task RemoveEntityAsync(
        string collection, string key, CancellationToken cancellationToken);

    /// <summary>
    /// Reads all entities from a collection as JsonElements.
    /// </summary>
    protected abstract Task<IEnumerable<(string Key, JsonElement Content)>> GetAllEntitiesAsJsonAsync(
        string collection, CancellationToken cancellationToken);

    #endregion

    #region IDocumentStore Implementation

    public abstract IEnumerable<string> InterestedCollection { get; }

    public async Task<Document?> GetDocumentAsync(string collection, string key, CancellationToken cancellationToken = default)
    {
        var content = await GetEntityAsJsonAsync(collection, key, cancellationToken);
        if (content == null) return null;

        // Try to get timestamp from metadata
        var metadata = await _context.Set<DocumentMetadataEntity>()
            .FirstOrDefaultAsync(m => m.Collection == collection && m.Key == key, cancellationToken);

        var timestamp = metadata != null
            ? new HlcTimestamp(metadata.HlcPhysicalTime, metadata.HlcLogicalCounter, metadata.HlcNodeId)
            : new HlcTimestamp(0, 0, "");

        return new Document(collection, key, content.Value, timestamp, metadata?.IsDeleted ?? false);
    }

    public async Task<IEnumerable<Document>> GetDocumentsByCollectionAsync(string collection, CancellationToken cancellationToken = default)
    {
        var entities = await GetAllEntitiesAsJsonAsync(collection, cancellationToken);
        
        // Batch-load metadata for this collection
        var metadataMap = await _context.Set<DocumentMetadataEntity>()
            .Where(m => m.Collection == collection)
            .ToDictionaryAsync(m => m.Key, cancellationToken);

        return entities.Select(e =>
        {
            var timestamp = metadataMap.TryGetValue(e.Key, out var meta)
                ? new HlcTimestamp(meta.HlcPhysicalTime, meta.HlcLogicalCounter, meta.HlcNodeId)
                : new HlcTimestamp(0, 0, "");
            return new Document(collection, e.Key, e.Content, timestamp, false);
        });
    }

    public async Task<IEnumerable<Document>> GetDocumentsAsync(List<(string Collection, string Key)> documentKeys, CancellationToken cancellationToken)
    {
        var documents = new List<Document>();
        foreach (var (collection, key) in documentKeys)
        {
            var doc = await GetDocumentAsync(collection, key, cancellationToken);
            if (doc != null)
            {
                documents.Add(doc);
            }
        }
        return documents;
    }

    public async Task<bool> PutDocumentAsync(Document document, CancellationToken cancellationToken = default)
    {
        await ApplyContentToEntityAsync(document.Collection, document.Key, document.Content, cancellationToken);

        if (!_isRemoteSync.Value)
        {
            await CreateOplogEntryAsync(document.Collection, document.Key, OperationType.Put, document.Content, cancellationToken);
        }

        return true;
    }

    public async Task<bool> UpdateBatchDocumentsAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        foreach (var document in documents)
        {
            await PutDocumentAsync(document, cancellationToken);
        }
        return true;
    }

    public async Task<bool> InsertBatchDocumentsAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        foreach (var document in documents)
        {
            await PutDocumentAsync(document, cancellationToken);
        }
        return true;
    }

    public async Task<bool> DeleteDocumentAsync(string collection, string key, CancellationToken cancellationToken = default)
    {
        await RemoveEntityAsync(collection, key, cancellationToken);

        if (!_isRemoteSync.Value)
        {
            await CreateOplogEntryAsync(collection, key, OperationType.Delete, null, cancellationToken);
        }

        return true;
    }

    public async Task<bool> DeleteBatchDocumentsAsync(IEnumerable<string> documentKeys, CancellationToken cancellationToken = default)
    {
        foreach (var key in documentKeys)
        {
            var parts = key.Split('/');
            if (parts.Length == 2)
            {
                await DeleteDocumentAsync(parts[0], parts[1], cancellationToken);
            }
            else
            {
                _logger.LogWarning("Invalid document key format: {Key}", key);
            }
        }
        return true;
    }

    public async Task<Document> MergeAsync(Document incoming, CancellationToken cancellationToken = default)
    {
        var existing = await GetDocumentAsync(incoming.Collection, incoming.Key, cancellationToken);

        if (existing == null)
        {
            await PutDocumentAsync(incoming, cancellationToken);
            return incoming;
        }

        var resolution = _conflictResolver.Resolve(existing, new OplogEntry(
            incoming.Collection,
            incoming.Key,
            OperationType.Put,
            incoming.Content,
            incoming.UpdatedAt,
            ""));

        if (resolution.ShouldApply && resolution.MergedDocument != null)
        {
            await PutDocumentAsync(resolution.MergedDocument, cancellationToken);
            return resolution.MergedDocument;
        }

        return existing;
    }

    #endregion

    #region ISnapshotable Implementation

    public async Task DropAsync(CancellationToken cancellationToken = default)
    {
        foreach (var collection in InterestedCollection)
        {
            var entities = await GetAllEntitiesAsJsonAsync(collection, cancellationToken);
            foreach (var (key, _) in entities)
            {
                await RemoveEntityAsync(collection, key, cancellationToken);
            }
        }
    }

    public async Task<IEnumerable<Document>> ExportAsync(CancellationToken cancellationToken = default)
    {
        var documents = new List<Document>();
        foreach (var collection in InterestedCollection)
        {
            var collectionDocs = await GetDocumentsByCollectionAsync(collection, cancellationToken);
            documents.AddRange(collectionDocs);
        }
        return documents;
    }

    public async Task ImportAsync(IEnumerable<Document> items, CancellationToken cancellationToken = default)
    {
        _isRemoteSync.Value = true;
        try
        {
            foreach (var document in items)
            {
                await PutDocumentAsync(document, cancellationToken);
            }
        }
        finally
        {
            _isRemoteSync.Value = false;
        }
    }

    public async Task MergeAsync(IEnumerable<Document> items, CancellationToken cancellationToken = default)
    {
        _isRemoteSync.Value = true;
        try
        {
            foreach (var document in items)
            {
                await MergeAsync(document, cancellationToken);
            }
        }
        finally
        {
            _isRemoteSync.Value = false;
        }
    }

    #endregion

    #region Oplog Management

    private HlcTimestamp GenerateTimestamp(string nodeId)
    {
        lock (_clockLock)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (now > _lastPhysicalTime)
            {
                _lastPhysicalTime = now;
                _logicalCounter = 0;
            }
            else
            {
                _logicalCounter++;
            }

            return new HlcTimestamp(_lastPhysicalTime, _logicalCounter, nodeId);
        }
    }

    private async Task CreateOplogEntryAsync(
        string collection,
        string key,
        OperationType operationType,
        JsonElement? content,
        CancellationToken cancellationToken)
    {
        var config = await _configProvider.GetConfiguration();
        var nodeId = config.NodeId;

        // Get last hash from OplogEntity table
        var lastEntry = await _context.Set<OplogEntity>()
            .Where(e => e.TimestampNodeId == nodeId)
            .OrderByDescending(e => e.TimestampPhysicalTime)
            .ThenByDescending(e => e.TimestampLogicalCounter)
            .FirstOrDefaultAsync(cancellationToken);

        var previousHash = lastEntry?.Hash ?? string.Empty;
        var timestamp = GenerateTimestamp(nodeId);

        var oplogEntry = new OplogEntry(
            collection,
            key,
            operationType,
            content,
            timestamp,
            previousHash);

        // Write OplogEntity
        _context.Set<OplogEntity>().Add(new OplogEntity
        {
            Collection = oplogEntry.Collection,
            Key = oplogEntry.Key,
            Operation = (int)oplogEntry.Operation,
            PayloadJson = oplogEntry.Payload?.GetRawText(),
            TimestampPhysicalTime = oplogEntry.Timestamp.PhysicalTime,
            TimestampLogicalCounter = oplogEntry.Timestamp.LogicalCounter,
            TimestampNodeId = oplogEntry.Timestamp.NodeId,
            Hash = oplogEntry.Hash,
            PreviousHash = oplogEntry.PreviousHash
        });

        // Write/Update DocumentMetadata
        var existingMetadata = await _context.Set<DocumentMetadataEntity>()
            .FirstOrDefaultAsync(m => m.Collection == collection && m.Key == key, cancellationToken);

        if (existingMetadata != null)
        {
            existingMetadata.HlcPhysicalTime = timestamp.PhysicalTime;
            existingMetadata.HlcLogicalCounter = timestamp.LogicalCounter;
            existingMetadata.HlcNodeId = timestamp.NodeId;
            existingMetadata.IsDeleted = operationType == OperationType.Delete;
        }
        else
        {
            _context.Set<DocumentMetadataEntity>().Add(new DocumentMetadataEntity
            {
                Collection = collection,
                Key = key,
                HlcPhysicalTime = timestamp.PhysicalTime,
                HlcLogicalCounter = timestamp.LogicalCounter,
                HlcNodeId = timestamp.NodeId,
                IsDeleted = operationType == OperationType.Delete
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Created Oplog entry: {Operation} {Collection}/{Key} at {Timestamp} (hash: {Hash})",
            operationType, collection, key, timestamp, oplogEntry.Hash);
    }

    /// <summary>
    /// Marks the start of remote sync operations (suppresses Oplog creation).
    /// Use in using statement: using (store.BeginRemoteSync()) { ... }
    /// </summary>
    public IDisposable BeginRemoteSync()
    {
        _isRemoteSync.Value = true;
        return new RemoteSyncScope();
    }

    private class RemoteSyncScope : IDisposable
    {
        public void Dispose()
        {
            _isRemoteSync.Value = false;
        }
    }

    #endregion

    public virtual void Dispose()
    {
    }
}
