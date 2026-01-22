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
    internal object CentralizedHashUpdateLock => new object();

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
