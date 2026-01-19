using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;

namespace EntglDb.Core;

/// <summary>
/// Main database interface for EntglDb, providing collection-based document storage with HLC timestamps.
/// </summary>
public class PeerDatabase : IPeerDatabase
{
    private readonly IPeerNodeConfigurationProvider _peerNodeConfigurationProvider;
    private readonly IPeerStore _store;
    private HlcTimestamp? _localClock;
    private readonly ConcurrentDictionary<string, PeerCollection> _collections = new ConcurrentDictionary<string, PeerCollection>();
    private readonly object _clockLock = new object();

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerDatabase"/> class.
    /// </summary>
    /// <param name="store">The persistence store for documents and oplog.</param>
    /// <param name="nodeId">The unique identifier for this node.</param>
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerDatabase"/> class.
    /// </summary>
    /// <param name="store">The persistence store for documents and oplog.</param>
    /// <param name="nodeId">The unique identifier for this node.</param>
    /// <param name="jsonOptions">Optional JSON serialization options.</param>
    public PeerDatabase(IPeerStore store, IPeerNodeConfigurationProvider peerNodeConfigurationProvider, JsonSerializerOptions? jsonOptions = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _peerNodeConfigurationProvider = peerNodeConfigurationProvider ?? throw new ArgumentNullException(nameof(peerNodeConfigurationProvider));
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions();
    }

    internal JsonSerializerOptions JsonOptions => _jsonOptions;

    /// <summary>
    /// Initializes the database by restoring the latest HLC timestamp from the store.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var storedInfo = await _store.GetLatestTimestampAsync(cancellationToken);
        var config = await _peerNodeConfigurationProvider.GetConfiguration();
        lock (_clockLock)
        {
            if (_localClock == null || storedInfo.CompareTo(_localClock!.Value) > 0)
            {
                _localClock = new HlcTimestamp(Math.Max(storedInfo.PhysicalTime, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), storedInfo.LogicalCounter, config.NodeId);
            }
        }
    }

    public IPeerCollection Collection(string name)
    {
        return _collections.GetOrAdd(name, n => new PeerCollection(n, this));
    }

    /// <summary>
    /// Gets a strongly-typed collection. The collection name defaults to the type name in lowercase.
    /// </summary>
    public IPeerCollection<T> Collection<T>(string? customName = null)
    {
        var mapper = EntglDbMapper.Global.Entity<T>();
        var collectionName = customName ?? mapper.CollectionName ?? typeof(T).Name.ToLowerInvariant();

        var attrProps = Metadata.EntityMetadata<T>.IndexedProperties.Select(p => p.Name).ToList();
        var mappedProps = mapper.IndexedProperties;
        var allProps = attrProps.Union(mappedProps).Distinct();

        if (allProps.Any())
        {
            Task.Run(async () =>
            {
                foreach (var p in allProps)
                {
                    try
                    {
                        await _store.EnsureIndexAsync(collectionName, p);
                    }
                    catch { /* Ignore index creation errors to prevent crashing app */ }
                }
            });
        }

        return new PeerCollection<T>(collectionName, this);
    }

    /// <summary>
    /// Manually triggers synchronization. Currently a no-op as sync is handled by SyncOrchestrator.
    /// </summary>
    public Task SyncAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        return _store.GetCollectionsAsync(cancellationToken);
    }

    internal IPeerStore Store => _store;

    internal async Task<HlcTimestamp> Tick()
    {
        var config = await _peerNodeConfigurationProvider.GetConfiguration();

        //assign initial value if null
        _localClock ??= new HlcTimestamp(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0, config.NodeId);

        lock (_clockLock)
        {
            long physicalNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long lastPhysical = _localClock!.Value.PhysicalTime;
            int logical = _localClock!.Value.LogicalCounter;

            if (physicalNow > lastPhysical)
            {
                _localClock = new HlcTimestamp(physicalNow, 0, config.NodeId);
            }
            else
            {
                _localClock = new HlcTimestamp(lastPhysical, logical + 1, config.NodeId);
            }
            return _localClock!.Value;
        }
    }
}

/// <summary>
/// Represents a collection of documents within a <see cref="PeerDatabase"/>.
/// </summary>
internal class PeerCollection : IPeerCollection
{
    private readonly string _name;
    private readonly PeerDatabase _db;

    public PeerCollection(string name, PeerDatabase db)
    {
        _name = name;
        _db = db;
    }

    public string Name => _name;

    public async Task Put(string key, object document, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.SerializeToElement(document, _db.JsonOptions);
        var timestamp = await _db.Tick();

        var oplog = new OplogEntry(_name, key, OperationType.Put, json, timestamp);
        var paramsContainer = new Document(_name, key, json, timestamp, false);

        await _db.Store.SaveDocumentAsync(paramsContainer, cancellationToken);
        await _db.Store.AppendOplogEntryAsync(oplog, cancellationToken);
    }

    public async Task PutMany(IEnumerable<KeyValuePair<string, object>> documents, CancellationToken cancellationToken = default)
    {
        var docList = new List<Document>();
        var oplogList = new List<OplogEntry>();

        foreach (var kvp in documents)
        {
            var key = kvp.Key;
            var document = kvp.Value;
            var json = JsonSerializer.SerializeToElement(document, _db.JsonOptions);
            var timestamp = await _db.Tick();

            var oplog = new OplogEntry(_name, key, OperationType.Put, json, timestamp);
            var paramsContainer = new Document(_name, key, json, timestamp, false);

            docList.Add(paramsContainer);
            oplogList.Add(oplog);
        }

        if (docList.Count > 0)
        {
            await _db.Store.ApplyBatchAsync(docList, oplogList, cancellationToken);
        }
    }

    public async Task<T> Get<T>(string key, CancellationToken cancellationToken = default)
    {
        var doc = await _db.Store.GetDocumentAsync(_name, key, cancellationToken);
        if (doc == null || doc.IsDeleted) return default;

        return JsonSerializer.Deserialize<T>(doc.Content, _db.JsonOptions);
    }

    public async Task Delete(string key, CancellationToken cancellationToken = default)
    {
        var timestamp = await _db.Tick();
        var oplog = new OplogEntry(_name, key, OperationType.Delete, null, timestamp);
        var empty = default(JsonElement);
        var paramsContainer = new Document(_name, key, empty, timestamp, true);

        await _db.Store.SaveDocumentAsync(paramsContainer, cancellationToken);
        await _db.Store.AppendOplogEntryAsync(oplog, cancellationToken);
    }

    public async Task DeleteMany(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var docList = new List<Document>();
        var oplogList = new List<OplogEntry>();
        var empty = default(JsonElement);

        foreach (var key in keys)
        {
            var timestamp = await _db.Tick();
            var oplog = new OplogEntry(_name, key, OperationType.Delete, null, timestamp);
            var paramsContainer = new Document(_name, key, empty, timestamp, true);

            docList.Add(paramsContainer);
            oplogList.Add(oplog);
        }

        if (docList.Count > 0)
        {
            await _db.Store.ApplyBatchAsync(docList, oplogList, cancellationToken);
        }
    }

    public async Task<IEnumerable<T>> Find<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await Find(predicate, null, null, null, true, cancellationToken);
    }

    public async Task<IEnumerable<T>> Find<T>(Expression<Func<T, bool>> predicate, int? skip, int? take, string? orderBy = null, bool ascending = true, CancellationToken cancellationToken = default)
    {
        QueryNode queryNode = null;
        try
        {
            queryNode = ExpressionToQueryNodeTranslator.Translate(predicate, _db.JsonOptions);
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[Warning] Query translation failed: {ex.Message}. Fetching all documents and filtering in memory.");
            // Passing null to store returns "1=1" (All documents)
        }

        var docs = await _db.Store.QueryDocumentsAsync(_name, queryNode, skip, take, orderBy, ascending, cancellationToken);
        var list = new List<T>();

        var compiledPredicate = queryNode == null && predicate != null ? predicate.Compile() : null;

        foreach (var d in docs)
        {
            if (!d.IsDeleted)
            {
                try
                {
                    var item = JsonSerializer.Deserialize<T>(d.Content, _db.JsonOptions);

                    // If query translation failed, we perform fallback filtering in memory.
                    // If translation succeeded, the Store has already filtered the content.
                    if (compiledPredicate != null)
                    {
                        if (compiledPredicate(item))
                        {
                            list.Add(item);
                        }
                    }
                    else
                    {
                        list.Add(item);
                    }
                }
                catch { /* deserialization error */ }
            }
        }
        return list;
    }

    public async Task<int> Count<T>(Expression<Func<T, bool>>? predicate, CancellationToken cancellationToken = default)
    {
        QueryNode? queryNode = null;
        if (predicate != null)
        {
            try
            {
                queryNode = ExpressionToQueryNodeTranslator.Translate(predicate, _db.JsonOptions);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[Warning] Query translation failed for Count: {ex.Message}. Falling back to counting in memory (inefficient).");
                var all = await Find(predicate, cancellationToken);
                return all.Count();
            }
        }

        return await _db.Store.CountDocumentsAsync(_name, queryNode, cancellationToken);
    }

    public Task<int> Count(Expression<Func<object, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        return Count<object>(predicate, cancellationToken);
    }
}

/// <summary>
/// Represents a strongly-typed collection of documents within a <see cref="PeerDatabase"/>.
/// </summary>
internal class PeerCollection<T> : IPeerCollection<T>
{
    private readonly PeerCollection _inner;

    public PeerCollection(string name, PeerDatabase db)
    {
        _inner = new PeerCollection(name, db);
    }

    public string Name => _inner.Name;

    public Task Put(string key, T document, CancellationToken cancellationToken = default)
        => _inner.Put(key, document, cancellationToken);

    public Task Put(T document, CancellationToken cancellationToken = default)
    {
        // Get key from entity metadata
        var getKey = Metadata.EntityMetadata<T>.GetKey;
        if (getKey == null)
            throw new InvalidOperationException(
                $"Type {typeof(T).Name} has no primary key defined. " +
                $"Add [PrimaryKey] attribute or define an 'Id' property, or use Put(key, document) instead.");

        var key = getKey(document);

        // Auto-generate if empty and auto-generation enabled
        if (string.IsNullOrEmpty(key) && Metadata.EntityMetadata<T>.AutoGenerateKey)
        {
            key = Guid.NewGuid().ToString();
            Metadata.EntityMetadata<T>.SetKey?.Invoke(document, key);
        }

        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException(
                $"Primary key for {typeof(T).Name} is null or empty. " +
                $"Ensure the primary key property has a value or enable auto-generation.");

        return Put(key, document, cancellationToken);
    }

    public Task PutMany(IEnumerable<T> documents, CancellationToken cancellationToken = default)
    {
        // Use metadata to extract keys and create KeyValuePair list for inner PutMany
        var list = new List<KeyValuePair<string, object>>();
        var getKey = Metadata.EntityMetadata<T>.GetKey;

        foreach (var document in documents)
        {
            if (document == null) throw new ArgumentNullException(nameof(documents), "Document cannot be null");

            if (getKey == null)
                throw new InvalidOperationException($"Type {typeof(T).Name} has no primary key defined.");

            var key = getKey(document);

            if (string.IsNullOrEmpty(key) && Metadata.EntityMetadata<T>.AutoGenerateKey)
            {
                key = Guid.NewGuid().ToString();
                Metadata.EntityMetadata<T>.SetKey?.Invoke(document, key);
            }

            if (string.IsNullOrEmpty(key))
                throw new InvalidOperationException($"Primary key for {typeof(T).Name} is null or empty.");

            list.Add(new KeyValuePair<string, object>(key, document));
        }

        return _inner.PutMany(list, cancellationToken);
    }

    public Task<T> Get(string key, CancellationToken cancellationToken = default)
        => _inner.Get<T>(key, cancellationToken);

    public Task Delete(string key, CancellationToken cancellationToken = default)
        => _inner.Delete(key, cancellationToken);

    public Task DeleteMany(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        => _inner.DeleteMany(keys, cancellationToken);

    public Task<IEnumerable<T>> Find(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => _inner.Find(predicate, cancellationToken);

    public Task<IEnumerable<T>> Find(Expression<Func<T, bool>> predicate, int? skip, int? take, string? orderBy = null, bool ascending = true, CancellationToken cancellationToken = default)
        => _inner.Find(predicate, skip, take, orderBy, ascending, cancellationToken);

    public Task<int> Count(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
        => _inner.Count(predicate, cancellationToken);

    // Explicit interface implementations for non-generic methods
    Task IPeerCollection.Put(string key, object document, CancellationToken cancellationToken)
        => _inner.Put(key, document, cancellationToken);

    Task IPeerCollection.PutMany(IEnumerable<KeyValuePair<string, object>> documents, CancellationToken cancellationToken)
        => _inner.PutMany(documents, cancellationToken);

    Task<TResult> IPeerCollection.Get<TResult>(string key, CancellationToken cancellationToken)
        => _inner.Get<TResult>(key, cancellationToken);

    Task IPeerCollection.Delete(string key, CancellationToken cancellationToken)
         => _inner.Delete(key, cancellationToken);

    Task IPeerCollection.DeleteMany(IEnumerable<string> keys, CancellationToken cancellationToken)
         => _inner.DeleteMany(keys, cancellationToken);

    Task<IEnumerable<TResult>> IPeerCollection.Find<TResult>(Expression<Func<TResult, bool>> predicate, CancellationToken cancellationToken)
        => _inner.Find(predicate, cancellationToken);

    Task<IEnumerable<TResult>> IPeerCollection.Find<TResult>(Expression<Func<TResult, bool>> predicate, int? skip, int? take, string? orderBy, bool ascending, CancellationToken cancellationToken)
        => _inner.Find(predicate, skip, take, orderBy, ascending, cancellationToken);

    Task<int> IPeerCollection.Count(Expression<Func<object, bool>>? predicate, CancellationToken cancellationToken)
        => _inner.Count(predicate, cancellationToken);
}
