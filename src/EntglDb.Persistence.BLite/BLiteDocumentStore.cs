using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BLite.Core;
using BLite.Core.CDC;
using BLite.Core.Collections;
using BLite.Core.Query;
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
/// Defers oplog creation to TASK-05 (FlushPendingChanges).
/// CDC events are converted to pending changes via IPendingChangesService.RecordChange.
/// </summary>
/// <typeparam name="TDbContext">The BLite DbContext type (user application context).</typeparam>
public abstract class BLiteDocumentStore<TDbContext> : IDocumentStore, IDisposable
    where TDbContext : DocumentDbContext
{
    protected readonly TDbContext _context;
    protected readonly EntglDbMetaContext _meta;
    protected readonly IPeerNodeConfigurationProvider _configProvider;
    protected readonly IConflictResolver _conflictResolver;
    protected readonly IVectorClockService _vectorClock;
    protected readonly IPendingChangesService _pendingChanges;
    protected readonly ILogger _logger;

    private readonly List<IDisposable> _cdcWatchers = new();
    private readonly HashSet<string> _registeredCollections = new();

    // HLC state for generating timestamps; used by GenerateTimestamp for sync path and pending changes
    private long _lastPhysicalTime;
    private int _logicalCounter;
    private readonly object _clockLock = new object();

    protected BLiteDocumentStore(
        TDbContext context,
        EntglDbMetaContext metaContext,
        IPeerNodeConfigurationProvider configProvider,
        IVectorClockService vectorClockService,
        IPendingChangesService pendingChangesService,
        IConflictResolver? conflictResolver = null,
        ILogger? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _meta = metaContext ?? throw new ArgumentNullException(nameof(metaContext));
        _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
        _vectorClock = vectorClockService ?? throw new ArgumentNullException(nameof(vectorClockService));
        _pendingChanges = pendingChangesService ?? throw new ArgumentNullException(nameof(pendingChangesService));
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
        IDocumentCollection<string, TEntity> collection,
        Func<TEntity, string> keySelector)
        where TEntity : class
    {
        _registeredCollections.Add(collectionName);
        
        var watcher = collection is DocumentCollection<string, TEntity> docCollection
            ? docCollection.Watch(capturePayload: true)
                .Subscribe(new CdcObserver<TEntity>(collectionName, keySelector, this))
            : null;
        if (watcher != null)
        {
            _cdcWatchers.Add(watcher);
        }
    }

    /// <summary>
    /// Generic CDC observer. Records BLite change events via IPendingChangesService.RecordChange.
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
            if (changeEvent.Type == BLiteOperationType.Delete)
            {
                var entityId = changeEvent.DocumentId?.ToString() ?? "";
                _store._pendingChanges.RecordChange(
                    _collectionName, entityId, OperationType.Delete,
                    _store.GenerateTimestamp());
            }
            else if (changeEvent.Entity != null)
            {
                var key = _keySelector(changeEvent.Entity);
                _store._pendingChanges.RecordChange(
                    _collectionName, key, OperationType.Put,
                    _store.GenerateTimestamp());
            }
        }

        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }

    #endregion

    #region Abstract Methods - Implemented by subclass

    /// <summary>
    /// Applies JSON content to a single entity (insert or update) and commits changes.
    /// Called for single-document operations.
    /// </summary>
    protected abstract Task ApplyContentToEntityAsync(
        string collection, string key, JsonElement content, CancellationToken cancellationToken);

    /// <summary>
    /// Applies JSON content to multiple entities (insert or update) with a single commit.
    /// Called for batch operations. Must commit all changes in a single SaveChanges.
    /// </summary>
    protected abstract Task ApplyContentToEntitiesBatchAsync(
        IEnumerable<(string Collection, string Key, JsonElement Content)> documents, CancellationToken cancellationToken);

    /// <summary>
    /// Reads an entity from the DbContext and returns it as JsonElement.
    /// </summary>
    protected abstract Task<JsonElement?> GetEntityAsJsonAsync(
        string collection, string key, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a single entity from the DbContext and commits changes.
    /// </summary>
    protected abstract Task RemoveEntityAsync(
        string collection, string key, CancellationToken cancellationToken);

    /// <summary>
    /// Removes multiple entities from the DbContext with a single commit.
    /// </summary>
    protected abstract Task RemoveEntitiesBatchAsync(
        IEnumerable<(string Collection, string Key)> documents, CancellationToken cancellationToken);

    /// <summary>
    /// Reads all entities from a collection as JsonElements.
    /// </summary>
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

        var metadataId = $"{collection}/{key}";
        var metadata = await _meta.DocumentMetadatas.FindByIdAsync(metadataId, cancellationToken);

        var timestamp = metadata != null
            ? new HlcTimestamp(metadata.HlcPhysicalTime, metadata.HlcLogicalCounter, metadata.HlcNodeId)
            : new HlcTimestamp(0, 0, "");

        return new Document(collection, key, content.Value, timestamp, metadata?.IsDeleted ?? false);
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
        await PutDocumentInternalAsync(document, cancellationToken);
        return true;
    }

    private async Task PutDocumentInternalAsync(Document document, CancellationToken cancellationToken)
    {
        await ApplyContentToEntityAsync(document.Collection, document.Key, document.Content, cancellationToken);

        // Keep DocumentMetadata in sync so GetDocumentAsync returns the correct timestamp.
        // This is critical for the LWW resolver to work correctly on future syncs.
        if (document.UpdatedAt.PhysicalTime > 0)
        {
            var metadataId = $"{document.Collection}/{document.Key}";
            var existingMetadata = await _meta.DocumentMetadatas.FindByIdAsync(metadataId, cancellationToken);

            if (existingMetadata != null)
            {
                // Only update if the incoming timestamp is newer
                if (document.UpdatedAt.PhysicalTime > existingMetadata.HlcPhysicalTime ||
                    (document.UpdatedAt.PhysicalTime == existingMetadata.HlcPhysicalTime &&
                     document.UpdatedAt.LogicalCounter > existingMetadata.HlcLogicalCounter))
                {
                    existingMetadata.HlcPhysicalTime = document.UpdatedAt.PhysicalTime;
                    existingMetadata.HlcLogicalCounter = document.UpdatedAt.LogicalCounter;
                    existingMetadata.HlcNodeId = document.UpdatedAt.NodeId;
                    existingMetadata.IsDeleted = document.IsDeleted;
                    existingMetadata.ContentHash = document.IsDeleted ? "" : EntityMappers.ComputeContentHash(document.Content);
                    await _meta.DocumentMetadatas.UpdateAsync(existingMetadata, cancellationToken);
                    await _meta.SaveChangesAsync(cancellationToken);
                }
            }
            else
            {
                await _meta.DocumentMetadatas.InsertAsync(
                    EntityMappers.CreateDocumentMetadata(document.Collection, document.Key, document.UpdatedAt, document.IsDeleted, document.Content),
                    cancellationToken);
                await _meta.SaveChangesAsync(cancellationToken);
            }
        }
    }

    public async Task<bool> UpdateBatchDocumentsAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        await ApplyContentToEntitiesBatchAsync(
            documents.Select(d => (d.Collection, d.Key, d.Content)), cancellationToken);
        return true;
    }

    public async Task<bool> InsertBatchDocumentsAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default)
    {
        await ApplyContentToEntitiesBatchAsync(
            documents.Select(d => (d.Collection, d.Key, d.Content)), cancellationToken);
        return true;
    }

    public async Task<bool> DeleteDocumentAsync(string collection, string key, CancellationToken cancellationToken = default)
    {
        await DeleteDocumentInternalAsync(collection, key, cancellationToken);
        return true;
    }

    private async Task DeleteDocumentInternalAsync(string collection, string key, CancellationToken cancellationToken)
    {
        await RemoveEntityAsync(collection, key, cancellationToken);
    }

    public async Task<bool> DeleteBatchDocumentsAsync(IEnumerable<string> documentKeys, CancellationToken cancellationToken = default)
    {
        var parsedKeys = new List<(string Collection, string Key)>();
        foreach (var key in documentKeys)
        {
            var parts = key.Split('/');
            if (parts.Length == 2)
            {
                parsedKeys.Add((parts[0], parts[1]));
            }
            else
            {
                _logger.LogWarning("Invalid document key format: {Key}", key);
            }
        }

        if (parsedKeys.Count == 0) return true;

        await RemoveEntitiesBatchAsync(parsedKeys, cancellationToken);
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
        await ApplyContentToEntitiesBatchAsync(
            items.Select(d => (d.Collection, d.Key, d.Content)), cancellationToken);
    }

    public async Task MergeAsync(IEnumerable<Document> items, CancellationToken cancellationToken = default)
    {
        foreach (var document in items)
        {
            await MergeAsync(document, cancellationToken);
        }
    }

    #endregion

    /// <summary>
    /// Generates an HLC timestamp for the local node.
    /// Used internally by CDC observers and sync paths.
    /// </summary>
    internal HlcTimestamp GenerateTimestamp()
    {
        var config = _configProvider.GetConfiguration().GetAwaiter().GetResult();
        var nodeId = config.NodeId;
        
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

    public virtual void Dispose()
    {
        foreach (var watcher in _cdcWatchers)
        {
            try { watcher.Dispose(); } catch { }
        }
        _cdcWatchers.Clear();
    }
}
