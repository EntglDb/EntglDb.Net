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
    private readonly ConcurrentDictionary<string, PeerCollection> _collections = new ConcurrentDictionary<string, PeerCollection>();
    private readonly object _clockLock = new object();

    private object _hashLock = new object();
    
    // CHANGED: Replaced broken "=> new object()" lock with a proper SemaphoreSlim for async coordination.
    // This ensures only one writer can append to the chain at a time.
    internal SemaphoreSlim WriteLock { get; } = new SemaphoreSlim(1, 1);

    private string? _lastHash;
    public string? LastHash
    {
        get
        {
            lock (_hashLock)
            {
                return _lastHash;
            }
        }
        internal set
        {
            lock (_hashLock)
            {
                _lastHash = value;
            }
        }
    }

    private HlcTimestamp? _localClock;

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
        
        _store.ChangesApplied += OnStoreChangesApplied;
    }

    private void OnStoreChangesApplied(object sender, ChangesAppliedEventArgs e)
    {
        if (e.Changes == null || !e.Changes.Any()) return;

        lock (_clockLock)
        {
            if (_localClock == null) return;

            foreach (var change in e.Changes)
            {
                var changeHlc = change.Timestamp;
                
                if (changeHlc.PhysicalTime > _localClock.Value.PhysicalTime)
                {
                    _localClock = new HlcTimestamp(changeHlc.PhysicalTime, changeHlc.LogicalCounter + 1, _localClock.Value.NodeId);
                }
                else if (changeHlc.PhysicalTime == _localClock.Value.PhysicalTime)
                {
                    if (changeHlc.LogicalCounter >= _localClock.Value.LogicalCounter)
                    {
                        _localClock = new HlcTimestamp(changeHlc.PhysicalTime, changeHlc.LogicalCounter + 1, _localClock.Value.NodeId);
                    }
                }
            }
        }
    }

    internal JsonSerializerOptions JsonOptions => _jsonOptions;

    /// <summary>
    /// Initializes the database by restoring the latest HLC timestamp from the store.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var config = await _peerNodeConfigurationProvider.GetConfiguration();
        var storedInfo = await _store.GetLatestTimestampAsync(cancellationToken);
        _lastHash = await _store.GetLastEntryHashAsync(config.NodeId, cancellationToken);
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

    public async Task PutAsync(string collection, string key, object document, CancellationToken cancellationToken = default)
    {
        await WriteLock.WaitAsync(cancellationToken);
        try
        {
            var json = JsonSerializer.SerializeToElement(document, _jsonOptions);
            var timestamp = await Tick();

            var previousHash = LastHash;
            var oplog = new OplogEntry(collection, key, OperationType.Put, json, timestamp, previousHash ?? string.Empty);
            
            var paramsContainer = new Document(collection, key, json, timestamp, false);

            await _store.SaveDocumentAsync(paramsContainer, cancellationToken);
            await _store.AppendOplogEntryAsync(oplog, cancellationToken);
            
            LastHash = oplog.Hash;
        }
        finally
        {
            WriteLock.Release();
        }
    }

    public async Task PutManyAsync(string collection, IEnumerable<KeyValuePair<string, object>> documents, CancellationToken cancellationToken = default)
    {
        await WriteLock.WaitAsync(cancellationToken);
        try
        {
            var docList = new List<Document>();
            var oplogList = new List<OplogEntry>();
            var firstTimestamp = await Tick();
            var nodeId = firstTimestamp.NodeId;
            var previousHash = LastHash;
            var newLastHash = previousHash;

            foreach (var kvp in documents)
            {
                var key = kvp.Key;
                var document = kvp.Value;
                var json = JsonSerializer.SerializeToElement(document, _jsonOptions);

                var timestamp = docList.Count == 0 ? firstTimestamp : new HlcTimestamp(firstTimestamp.PhysicalTime, firstTimestamp.LogicalCounter + docList.Count, nodeId);

                var oplog = new OplogEntry(collection, key, OperationType.Put, json, timestamp, previousHash ?? string.Empty);
                previousHash = oplog.Hash;
                newLastHash = oplog.Hash;

                var paramsContainer = new Document(collection, key, json, timestamp, false);

                docList.Add(paramsContainer);
                oplogList.Add(oplog);
            }

            if (docList.Count > 0)
            {
                await _store.ApplyBatchAsync(docList, oplogList, cancellationToken);
                LastHash = newLastHash;
            }
        }
        finally
        {
            WriteLock.Release();
        }
    }

    public async Task<T?> GetAsync<T>(string collection, string key, CancellationToken cancellationToken = default)
    {
        var doc = await _store.GetDocumentAsync(collection, key, cancellationToken);
        if (doc == null || doc.IsDeleted) return default;

        return JsonSerializer.Deserialize<T>(doc.Content, _jsonOptions);
    }

    public async Task DeleteAsync(string collection, string key, CancellationToken cancellationToken = default)
    {
        await WriteLock.WaitAsync(cancellationToken);
        try
        {
            var timestamp = await Tick();
            var previousHash = LastHash;
            
            var oplog = new OplogEntry(collection, key, OperationType.Delete, null, timestamp, previousHash ?? string.Empty);

            var empty = default(JsonElement);
            var paramsContainer = new Document(collection, key, empty, timestamp, true);
            
            await _store.SaveDocumentAsync(paramsContainer, cancellationToken);
            await _store.AppendOplogEntryAsync(oplog, cancellationToken);
            
            LastHash = oplog.Hash;
        }
        finally
        {
            WriteLock.Release();
        }
    }

    public async Task DeleteManyAsync(string collection, IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        await WriteLock.WaitAsync(cancellationToken);
        try
        {
            var docList = new List<Document>();
            var oplogList = new List<OplogEntry>();
            var empty = default(JsonElement);
            
            var firstTimestamp = await Tick();
            var nodeId = firstTimestamp.NodeId;
            var previousHash = LastHash;
            var newLastHash = previousHash;

            foreach (var key in keys)
            {
                var timestamp = docList.Count == 0 ? firstTimestamp : new HlcTimestamp(firstTimestamp.PhysicalTime, firstTimestamp.LogicalCounter + docList.Count, nodeId);

                var oplog = new OplogEntry(collection, key, OperationType.Delete, null, timestamp, previousHash ?? string.Empty);
                previousHash = oplog.Hash;
                newLastHash = oplog.Hash;

                var paramsContainer = new Document(collection, key, empty, timestamp, true);

                docList.Add(paramsContainer);
                oplogList.Add(oplog);
            }

            if (docList.Count > 0)
            {
                await _store.ApplyBatchAsync(docList, oplogList, cancellationToken);
                LastHash = newLastHash;
            }
        }
        finally
        {
            WriteLock.Release();
        }
    }

    public async Task<IEnumerable<T>> FindAsync<T>(string collection, Expression<Func<T, bool>> predicate, int? skip = null, int? take = null, string? orderBy = null, bool ascending = true, CancellationToken cancellationToken = default)
    {
        QueryNode? queryNode = null;
        try
        {
            queryNode = ExpressionToQueryNodeTranslator.Translate(predicate, _jsonOptions);
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"[Warning] Query translation failed: {ex.Message}. Fetching all documents and filtering in memory.");
        }

        var docs = await _store.QueryDocumentsAsync(collection, queryNode, skip, take, orderBy, ascending, cancellationToken);
        var list = new List<T>();

        var compiledPredicate = queryNode == null && predicate != null ? predicate.Compile() : null;

        foreach (var d in docs)
        {
            if (!d.IsDeleted)
            {
                try
                {
                    var item = JsonSerializer.Deserialize<T>(d.Content, _jsonOptions);
                    if (item == null) continue;

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

    public async Task<int> CountAsync<T>(string collection, Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        QueryNode? queryNode = null;
        if (predicate != null)
        {
            try
            {
                queryNode = ExpressionToQueryNodeTranslator.Translate(predicate, _jsonOptions);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[Warning] Query translation failed for Count: {ex.Message}. Falling back to counting in memory.");
                var all = await FindAsync(collection, predicate, cancellationToken: cancellationToken);
                return all.Count();
            }
        }

        return await _store.CountDocumentsAsync(collection, queryNode, cancellationToken);
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
