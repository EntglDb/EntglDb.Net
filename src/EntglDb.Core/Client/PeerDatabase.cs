using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core.Storage;

namespace EntglDb.Core
{
    /// <summary>
    /// Main database interface for EntglDb, providing collection-based document storage with HLC timestamps.
    /// </summary>
    public class PeerDatabase : IPeerDatabase
    {
        private readonly IPeerStore _store;
        private readonly string _nodeId;
        private HlcTimestamp _localClock;
        private readonly ConcurrentDictionary<string, PeerCollection> _collections = new ConcurrentDictionary<string, PeerCollection>();
        private readonly object _clockLock = new object();

        /// <summary>
        /// Initializes a new instance of the <see cref="PeerDatabase"/> class.
        /// </summary>
        /// <param name="store">The persistence store for documents and oplog.</param>
        /// <param name="nodeId">The unique identifier for this node.</param>
        public PeerDatabase(IPeerStore store, string nodeId = "local")
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _nodeId = nodeId;
            _localClock = new HlcTimestamp(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0, _nodeId);
        }

        /// <summary>
        /// Initializes the database by restoring the latest HLC timestamp from the store.
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default) 
        {
             var storedInfo = await _store.GetLatestTimestampAsync(cancellationToken);
             lock (_clockLock)
             {
                 if (storedInfo.CompareTo(_localClock) > 0)
                 {
                     _localClock = new HlcTimestamp(Math.Max(storedInfo.PhysicalTime, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), storedInfo.LogicalCounter, _nodeId);
                 }
             }
        }

        public IPeerCollection Collection(string name)
        {
            return _collections.GetOrAdd(name, n => new PeerCollection(n, this));
        }

        /// <summary>
        /// Manually triggers synchronization. Currently a no-op as sync is handled by SyncOrchestrator.
        /// </summary>
        public Task SyncAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        internal IPeerStore Store => _store;
        
        internal HlcTimestamp Tick()
        {
            lock (_clockLock)
            {
                long physicalNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                long lastPhysical = _localClock.PhysicalTime;
                int logical = _localClock.LogicalCounter;

                if (physicalNow > lastPhysical)
                {
                    _localClock = new HlcTimestamp(physicalNow, 0, _nodeId);
                }
                else
                {
                    _localClock = new HlcTimestamp(lastPhysical, logical + 1, _nodeId);
                }
                return _localClock;
            }
        }
    }

    /// <summary>
    /// Represents a collection of documents within a <see cref="PeerDatabase"/>.
    /// </summary>
    public class PeerCollection : IPeerCollection
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
            var json = JsonSerializer.SerializeToElement(document);
            var timestamp = _db.Tick();

            var oplog = new OplogEntry(_name, key, OperationType.Put, json, timestamp);
            var paramsContainer = new Document(_name, key, json, timestamp, false);

            await _db.Store.SaveDocumentAsync(paramsContainer, cancellationToken);
            await _db.Store.AppendOplogEntryAsync(oplog, cancellationToken);
        }

        public async Task<T> Get<T>(string key, CancellationToken cancellationToken = default)
        {
            var doc = await _db.Store.GetDocumentAsync(_name, key, cancellationToken);
            if (doc == null || doc.IsDeleted) return default;

            return JsonSerializer.Deserialize<T>(doc.Content);
        }

        public async Task Delete(string key, CancellationToken cancellationToken = default)
        {
            var timestamp = _db.Tick();
            var oplog = new OplogEntry(_name, key, OperationType.Delete, null, timestamp);
            var empty = default(JsonElement);
            var paramsContainer = new Document(_name, key, empty, timestamp, true);

            await _db.Store.SaveDocumentAsync(paramsContainer, cancellationToken);
            await _db.Store.AppendOplogEntryAsync(oplog, cancellationToken);
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
                queryNode = ExpressionToQueryNodeTranslator.Translate(predicate);
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
                        var item = JsonSerializer.Deserialize<T>(d.Content);
                        
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
    }
}
