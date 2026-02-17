using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Core.Storage.Events;
using EntglDb.Core.Sync;
using EntglDb.Persistence.BLite.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    /// Thread-local flag to suppress Oplog creation during remote sync operations.
    /// When true, write to DB only (Oplog entry already exists from remote).
    /// </summary>
    private static readonly AsyncLocal<bool> _isRemoteSync = new AsyncLocal<bool>();

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

    public abstract IEnumerable<string> InterestedCollection { get; }

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
        // Apply to DbContext
        await ApplyContentToEntityAsync(document.Collection, document.Key, document.Content, cancellationToken);

        // Create Oplog entry ONLY if this is a local change (not from remote sync)
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
        // Remove from DbContext
        await RemoveEntityAsync(collection, key, cancellationToken);

        // Create Oplog entry ONLY if this is a local change
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
            await PutDocumentAsync(incoming, cancellationToken);
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
        // Set remote sync flag to prevent Oplog creation
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
        // Set remote sync flag to prevent Oplog creation
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

    #region Events (Deprecated - kept for backward compatibility)

    // These events are no longer used internally but kept for backward compatibility
    public event EventHandler<DocumentsDeletedEventArgs>? DocumentsDeleted;
    public event EventHandler<DocumentsInsertedEventArgs>? DocumentsInserted;
    public event EventHandler<DocumentsUpdatedEventArgs>? DocumentsUpdated;

    #endregion

    public virtual void Dispose()
    {
        // Subclasses can override to dispose resources
    }
}
