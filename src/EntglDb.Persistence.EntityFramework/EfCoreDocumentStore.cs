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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Persistence.EntityFramework;

/// <summary>
/// Abstract base class for EF Core-based document stores.
/// CDC events are synchronously forwarded to IPendingChangesService.RecordChange.
/// Oplog creation is deferred to FlushPendingChanges (TASK-05).
/// DocumentMetadata is stored externally via IDocumentMetadataStore (e.g., BLiteDocumentMetadataStore).
/// </summary>
/// <typeparam name="TDbContext">The EF Core DbContext type.</typeparam>
public abstract class EfCoreDocumentStore<TDbContext> : IDocumentStore, IDisposable
    where TDbContext : DbContext
{
    protected readonly TDbContext _context;
    protected readonly IDocumentMetadataStore _metadataStore;
    protected readonly IPeerNodeConfigurationProvider _configProvider;
    protected readonly IConflictResolver _conflictResolver;
    protected readonly ILogger _logger;

    private readonly IPendingChangesService _pendingChanges;
    private readonly EventHandler<SavingChangesEventArgs> _savingChangesHandler;

    // NodeId is fixed at runtime — cached on first use to avoid repeated blocking calls.
    private readonly Lazy<string> _nodeId;

    // HLC state for generating timestamps; kept for synchronous CDC path and GenerateTimestamp
    private long _lastPhysicalTime;
    private int _logicalCounter;
    private readonly object _clockLock = new object();

    protected EfCoreDocumentStore(
        TDbContext context,
        IDocumentMetadataStore metadataStore,
        IPendingChangesService pendingChangesService,
        IPeerNodeConfigurationProvider configProvider,
        IConflictResolver? conflictResolver = null,
        ILogger? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _metadataStore = metadataStore ?? throw new ArgumentNullException(nameof(metadataStore));
        _pendingChanges = pendingChangesService ?? throw new ArgumentNullException(nameof(pendingChangesService));
        _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
        _conflictResolver = conflictResolver ?? new LastWriteWinsConflictResolver();
        _logger = logger ?? NullLogger.Instance;

        _lastPhysicalTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _logicalCounter = 0;
        _nodeId = new Lazy<string>(() => _configProvider.GetConfiguration().GetAwaiter().GetResult().NodeId);

        // Synchronous CDC: record changes before SaveChanges commits.
        // No SavedChanges / async fire-and-forget — thread-safe by design.
        _savingChangesHandler = (_, _) =>
        {
            foreach (var entry in _context.ChangeTracker.Entries())
            {
                if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                    continue;

                var collKey = GetCollectionAndKey(entry.Entity);
                if (collKey == null) continue;

                var (collection, key) = collKey.Value;
                var opType = entry.State == EntityState.Deleted ? OperationType.Delete : OperationType.Put;
                _pendingChanges.RecordChange(collection, key, opType, GenerateTimestamp());
            }
        };
        _context.SavingChanges += _savingChangesHandler;
    }

    #region Abstract Methods - Implemented by subclass

    /// <summary>
    /// Applies JSON content to a single entity (insert or update) and commits changes.
    /// </summary>
    protected abstract Task ApplyContentToEntityAsync(
        string collection, string key, JsonElement content, CancellationToken cancellationToken);

    /// <summary>
    /// Applies JSON content to multiple entities (insert or update) with a single commit.
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

    /// <summary>
    /// Maps an EF Core entity instance to its logical (Collection, Key) tuple.
    /// Return null for entities that should not trigger CDC recording.
    /// </summary>
    protected abstract (string Collection, string Key)? GetCollectionAndKey(object entity);

    #endregion

    #region IDocumentStore Implementation

    public abstract IEnumerable<string> InterestedCollection { get; }

    public async Task<Document?> GetDocumentAsync(string collection, string key, CancellationToken cancellationToken = default)
    {
        var content = await GetEntityAsJsonAsync(collection, key, cancellationToken);
        if (content == null) return null;

        var metadata = await _metadataStore.GetMetadataAsync(collection, key, cancellationToken);

        var timestamp = metadata != null
            ? metadata.UpdatedAt
            : new HlcTimestamp(0, 0, "");

        return new Document(collection, key, content.Value, timestamp, metadata?.IsDeleted ?? false);
    }

    public async Task<IEnumerable<Document>> GetDocumentsByCollectionAsync(string collection, CancellationToken cancellationToken = default)
    {
        var entities = await GetAllEntitiesAsJsonAsync(collection, cancellationToken);

        var metadataList = await _metadataStore.GetMetadataByCollectionAsync(collection, cancellationToken);
        var metadataMap = metadataList.ToDictionary(m => m.Key);

        return entities.Select(e =>
        {
            var timestamp = metadataMap.TryGetValue(e.Key, out var meta)
                ? meta.UpdatedAt
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
                documents.Add(doc);
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

        if (document.UpdatedAt.PhysicalTime > 0)
        {
            var existing = await _metadataStore.GetMetadataAsync(document.Collection, document.Key, cancellationToken);

            if (existing == null ||
                document.UpdatedAt.PhysicalTime > existing.UpdatedAt.PhysicalTime ||
                (document.UpdatedAt.PhysicalTime == existing.UpdatedAt.PhysicalTime &&
                 document.UpdatedAt.LogicalCounter > existing.UpdatedAt.LogicalCounter))
            {
                await _metadataStore.UpsertMetadataAsync(new DocumentMetadata(
                    document.Collection,
                    document.Key,
                    document.UpdatedAt,
                    document.IsDeleted), cancellationToken);
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
        await RemoveEntityAsync(collection, key, cancellationToken);
        return true;
    }

    public async Task<bool> DeleteBatchDocumentsAsync(IEnumerable<string> documentKeys, CancellationToken cancellationToken = default)
    {
        var parsedKeys = new List<(string Collection, string Key)>();
        foreach (var key in documentKeys)
        {
            var parts = key.Split('/');
            if (parts.Length == 2)
                parsedKeys.Add((parts[0], parts[1]));
            else
                _logger.LogWarning("Invalid document key format: {Key}", key);
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
            await PutDocumentInternalAsync(incoming, cancellationToken);
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
                await RemoveEntityAsync(collection, key, cancellationToken);
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
            await MergeAsync(document, cancellationToken);
    }

    #endregion

    /// <summary>
    /// Generates an HLC timestamp for the local node.
    /// Used by the synchronous CDC handler and sync paths.
    /// </summary>
    protected HlcTimestamp GenerateTimestamp()
    {
        var nodeId = _nodeId.Value;

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
        _context.SavingChanges -= _savingChangesHandler;
    }
}

