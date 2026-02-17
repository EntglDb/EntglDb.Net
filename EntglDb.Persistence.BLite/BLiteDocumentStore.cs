using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BLite.Core.CDC;
using BLite.Core.Collections;
using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Persistence.BLite.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using BLiteOperationType = BLite.Core.Transactions.OperationType;

namespace EntglDb.Persistence.BLite;

/// <summary>
/// Abstract base class for BLite-based document stores.
/// Handles Oplog creation internally - subclasses only implement entity mapping.
/// </summary>
/// <typeparam name="TDbContext">The BLite DbContext type.</typeparam>
public abstract class BLiteDocumentStore<TDbContext> : IDocumentStore, IDisposable
    where TDbContext : EntglDocumentDbContext
{
    protected readonly TDbContext _context;
    protected readonly IPeerNodeConfigurationProvider _configProvider;
    protected readonly IConflictResolver _conflictResolver;
    protected readonly ILogger _logger;

    /// <summary>
    /// Semaphore used to suppress CDC-triggered OplogEntry creation during remote sync.
    /// CurrentCount == 0 ? sync in progress, CDC must skip.
    /// CurrentCount == 1 ? no sync, CDC creates OplogEntry.
    /// </summary>
    private readonly SemaphoreSlim _remoteSyncGuard = new SemaphoreSlim(1, 1);

    private readonly List<IDisposable> _cdcWatchers = new();
    private readonly HashSet<string> _registeredCollections = new();

    // HLC state for generating timestamps for local changes
    private long _lastPhysicalTime;
    private int _logicalCounter;
    private readonly object _clockLock = new object();

    protected BLiteDocumentStore(
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

    #region CDC Registration

    /// <summary>
    /// Registers a BLite collection for CDC tracking.
    /// Call in subclass constructor for each collection to sync.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="collectionName">The logical collection name used in Oplog.</param>
    /// <param name="collection">The BLite DocumentCollection.</param>
    /// <param name="keySelector">Function to extract the entity key.</param>
    protected void WatchCollection<TEntity>(
        string collectionName,
        DocumentCollection<string, TEntity> collection,
        Func<TEntity, string> keySelector)
        where TEntity : class
    {
        _registeredCollections.Add(collectionName);
        
        var watcher = collection.Watch(capturePayload: true)
            .Subscribe(new CdcObserver<TEntity>(collectionName, keySelector, this));
        _cdcWatchers.Add(watcher);
    }

    /// <summary>
    /// Generic CDC observer. Forwards BLite change events to OnLocalChangeDetectedAsync.
    /// Automatically skips events when remote sync is in progress.
    /// </summary>
    private class CdcObserver<TEntity> : IObserver<ChangeStreamEvent<string, TEntity>>
        where TEntity : class
    {
        private readonly string _collectionName;
        private readonly Func<TEntity, string> _keySelector;
        private readonly BLiteDocumentStore<TDbContext> _store;

        public CdcObserver(
            string collectionName,
            Func<TEntity, string> keySelector,
            BLiteDocumentStore<TDbContext> store)
        {
            _collectionName = collectionName;
            _keySelector = keySelector;
            _store = store;
        }

        public void OnNext(ChangeStreamEvent<string, TEntity> changeEvent)
        {
            if (_store._remoteSyncGuard.CurrentCount == 0) return;

            var entityId = changeEvent.DocumentId?.ToString() ?? "";

            if (changeEvent.Type == BLiteOperationType.Delete)
            {
                _store.OnLocalChangeDetectedAsync(_collectionName, entityId, OperationType.Delete, null)
                    .GetAwaiter().GetResult();
            }
            else if (changeEvent.Entity != null)
            {
                var content = JsonSerializer.SerializeToElement(changeEvent.Entity);
                var key = _keySelector(changeEvent.Entity);
                _store.OnLocalChangeDetectedAsync(_collectionName, key, OperationType.Put, content)
                    .GetAwaiter().GetResult();
            }
        }

        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }

    #endregion

    #region Abstract Methods - Implemented by subclass

    /// <summary>
    /// Applies JSON content to an entity in the DbContext (insert or update).
    /// </summary>
    /// <param name="collection">The collection name.</param>
    /// <param name="key">The entity key.</param>
    /// <param name="content">The JSON content to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected abstract Task ApplyContentToEntityAsync(
        string collection, string key, JsonElement content, CancellationToken cancellationToken);

    /// <summary>
    /// Reads an entity from the DbContext and returns it as JsonElement.
    /// </summary>
    /// <param name="collection">The collection name.</param>
    /// <param name="key">The entity key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>JsonElement if found, null otherwise.</returns>
    protected abstract Task<JsonElement?> GetEntityAsJsonAsync(
        string collection, string key, CancellationToken cancellationToken);

    /// <summary>
    /// Removes an entity from the DbContext.
    /// </summary>
    /// <param name="collection">The collection name.</param>
    /// <param name="key">The entity key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected abstract Task RemoveEntityAsync(
        string collection, string key, CancellationToken cancellationToken);

    /// <summary>
    /// Reads all entities from a collection as JsonElements.
    /// </summary>
    /// <param name="collection">The collection name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Enumerable of (Key, JsonElement) pairs.</returns>
    protected abstract Task<IEnumerable<(string Key, JsonElement Content)>> GetAllEntitiesAsJsonAsync(
        string collection, CancellationToken cancellationToken);

    #endregion

    #region IDocumentStore Implementation

    /// <summary>
    /// Returns the collections registered via WatchCollection.
    /// </summary>
    public IEnumerable<string> InterestedCollection => _registeredCollections;

    public async Task<Document?> GetDocumentAsync(string collection, string key, CancellationToken cancellationToken = default)
    {
        var content = await GetEntityAsJsonAsync(collection, key, cancellationToken);
        if (content == null) return null;

        var timestamp = new HlcTimestamp(0, 0, ""); // Will be populated from metadata if needed
        return new Document(collection, key, content.Value, timestamp, false);
    }

    public async Task<IEnumerable<Document>> GetDocumentsByCollectionAsync(string collection, CancellationToken cancellationToken = default)
    {
        var entities = await GetAllEntitiesAsJsonAsync(collection, cancellationToken);
        var timestamp = new HlcTimestamp(0, 0, "");
        return entities.Select(e => new Document(collection, e.Key, e.Content, timestamp, false));
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
        await _remoteSyncGuard.WaitAsync(cancellationToken);
        try
        {
            await PutDocumentInternalAsync(document, cancellationToken);
        }
        finally
        {
            _remoteSyncGuard.Release();
        }
        return true;
    }

    private async Task PutDocumentInternalAsync(Document document, CancellationToken cancellationToken)
    {
        await ApplyContentToEntityAsync(document.Collection, document.Key, document.Content, cancellationToken);
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
        await _remoteSyncGuard.WaitAsync(cancellationToken);
        try
        {
            await DeleteDocumentInternalAsync(collection, key, cancellationToken);
        }
        finally
        {
            _remoteSyncGuard.Release();
        }
        return true;
    }

    private async Task DeleteDocumentInternalAsync(string collection, string key, CancellationToken cancellationToken)
    {
        await RemoveEntityAsync(collection, key, cancellationToken);
    }

    public async Task<bool> DeleteBatchDocumentsAsync(IEnumerable<string> documentKeys, CancellationToken cancellationToken = default)
    {
        foreach (var key in documentKeys)
        {
            // Extract collection from key format "collection/key" or assume single collection
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
            // Use internal method - guard not acquired yet in single-document merge
            await PutDocumentInternalAsync(incoming, cancellationToken);
            return incoming;
        }

        // Use conflict resolver to merge
        var resolution = _conflictResolver.Resolve(existing, new OplogEntry(
            incoming.Collection,
            incoming.Key,
            OperationType.Put,
            incoming.Content,
            incoming.UpdatedAt,
            ""));

        if (resolution.ShouldApply && resolution.MergedDocument != null)
        {
            await PutDocumentInternalAsync(resolution.MergedDocument, cancellationToken);
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
        // Acquire guard to prevent Oplog creation during import
        await _remoteSyncGuard.WaitAsync(cancellationToken);
        try
        {
            foreach (var document in items)
            {
                await PutDocumentInternalAsync(document, cancellationToken);
            }
        }
        finally
        {
            _remoteSyncGuard.Release();
        }
    }

    public async Task MergeAsync(IEnumerable<Document> items, CancellationToken cancellationToken = default)
    {
        // Acquire guard to prevent Oplog creation during merge
        await _remoteSyncGuard.WaitAsync(cancellationToken);
        try
        {
            foreach (var document in items)
            {
                await MergeAsync(document, cancellationToken);
            }
        }
        finally
        {
            _remoteSyncGuard.Release();
        }
    }

    #endregion

    #region Oplog Management

    /// <summary>
    /// Returns true if a remote sync operation is in progress (guard acquired).
    /// CDC listeners should check this before creating OplogEntry.
    /// </summary>
    protected bool IsRemoteSyncInProgress => _remoteSyncGuard.CurrentCount == 0;

    /// <summary>
    /// Called by subclass CDC listeners when a local change is detected.
    /// Creates OplogEntry + DocumentMetadata only if no remote sync is in progress.
    /// </summary>
    protected async Task OnLocalChangeDetectedAsync(
        string collection,
        string key,
        OperationType operationType,
        JsonElement? content,
        CancellationToken cancellationToken = default)
    {
        if (IsRemoteSyncInProgress) return;

        await CreateOplogEntryAsync(collection, key, operationType, content, cancellationToken);
    }

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

        // Get last hash from OplogEntries collection directly
        var lastEntry = _context.OplogEntries
            .Find(e => e.TimestampNodeId == nodeId)
            .OrderByDescending(e => e.TimestampPhysicalTime)
            .ThenByDescending(e => e.TimestampLogicalCounter)
            .FirstOrDefault();

        var previousHash = lastEntry?.Hash ?? string.Empty;
        var timestamp = GenerateTimestamp(nodeId);

        var oplogEntry = new OplogEntry(
            collection,
            key,
            operationType,
            content,
            timestamp,
            previousHash);

        // Write directly to OplogEntries collection
        await _context.OplogEntries.InsertAsync(oplogEntry.ToEntity());

        // Write DocumentMetadata for sync tracking
        var docMetadata = EntityMappers.CreateDocumentMetadata(
            collection, 
            key, 
            timestamp, 
            isDeleted: operationType == OperationType.Delete);

        var existingMetadata = _context.DocumentMetadatas
            .Find(m => m.Collection == collection && m.Key == key)
            .FirstOrDefault();

        if (existingMetadata != null)
        {
            // Update existing metadata
            existingMetadata.HlcPhysicalTime = timestamp.PhysicalTime;
            existingMetadata.HlcLogicalCounter = timestamp.LogicalCounter;
            existingMetadata.HlcNodeId = timestamp.NodeId;
            existingMetadata.IsDeleted = operationType == OperationType.Delete;
            await _context.DocumentMetadatas.UpdateAsync(existingMetadata);
        }
        else
        {
            await _context.DocumentMetadatas.InsertAsync(docMetadata);
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Created Oplog entry: {Operation} {Collection}/{Key} at {Timestamp} (hash: {Hash})",
            operationType, collection, key, timestamp, oplogEntry.Hash);
    }

    /// <summary>
    /// Marks the start of remote sync operations (suppresses CDC-triggered Oplog creation).
    /// Use in using statement: using (store.BeginRemoteSync()) { ... }
    /// </summary>
    public IDisposable BeginRemoteSync()
    {
        _remoteSyncGuard.Wait();
        return new RemoteSyncScope(_remoteSyncGuard);
    }

    private class RemoteSyncScope : IDisposable
    {
        private readonly SemaphoreSlim _guard;

        public RemoteSyncScope(SemaphoreSlim guard)
        {
            _guard = guard;
        }

        public void Dispose()
        {
            _guard.Release();
        }
    }

    #endregion

    public virtual void Dispose()
    {
        foreach (var watcher in _cdcWatchers)
        {
            try { watcher.Dispose(); } catch { }
        }
        _cdcWatchers.Clear();
        _remoteSyncGuard.Dispose();
    }
}
