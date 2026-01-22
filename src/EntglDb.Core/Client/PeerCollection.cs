using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Core;

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
        OplogEntry oplog;

        var json = JsonSerializer.SerializeToElement(document, _db.JsonOptions);
        var timestamp = await _db.Tick();

        lock (_db.CentralizedHashUpdateLock)
        {
            var previousHash = _db.LastHash;
            oplog = new OplogEntry(_name, key, OperationType.Put, json, timestamp, previousHash ?? string.Empty);
            _db.LastHash = oplog.Hash;
        }

        var paramsContainer = new Document(_name, key, json, timestamp, false);

        await _db.Store.SaveDocumentAsync(paramsContainer, cancellationToken);
        await _db.Store.AppendOplogEntryAsync(oplog, cancellationToken);
    }

    public async Task PutMany(IEnumerable<KeyValuePair<string, object>> documents, CancellationToken cancellationToken = default)
    {
        var docList = new List<Document>();
        var oplogList = new List<OplogEntry>();
        var firstTimestamp = await _db.Tick();
        var nodeId = firstTimestamp.NodeId;

        lock (_db.CentralizedHashUpdateLock)
        {
            var previousHash = _db.LastHash;

            foreach (var kvp in documents)
            {
                var key = kvp.Key;
                var document = kvp.Value;
                var json = JsonSerializer.SerializeToElement(document, _db.JsonOptions);

                // We need unique logical timestamps for each entry in the batch
                // The first one is already ticked. Subsequent ones increment logic counter.
                var timestamp = docList.Count == 0 ? firstTimestamp : new HlcTimestamp(firstTimestamp.PhysicalTime, firstTimestamp.LogicalCounter + docList.Count, nodeId);

                var oplog = new OplogEntry(_name, key, OperationType.Put, json, timestamp, previousHash ?? string.Empty);
                previousHash = oplog.Hash; // Update for next item in batch

                var paramsContainer = new Document(_name, key, json, timestamp, false);

                docList.Add(paramsContainer);
                oplogList.Add(oplog);
            }

            _db.LastHash = previousHash;
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
        OplogEntry oplog;
        var timestamp = await _db.Tick();

        lock (_db.CentralizedHashUpdateLock)
        {
            var previousHash = _db.LastHash;

            oplog = new OplogEntry(_name, key, OperationType.Delete, null, timestamp, previousHash ?? string.Empty);

            _db.LastHash = oplog.Hash;
        }

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
        
        var firstTimestamp = await _db.Tick();
        var nodeId = firstTimestamp.NodeId;

        lock (_db.CentralizedHashUpdateLock)
        {
            var previousHash = _db.LastHash;

            foreach (var key in keys)
            {
                var timestamp = docList.Count == 0 ? firstTimestamp : new HlcTimestamp(firstTimestamp.PhysicalTime, firstTimestamp.LogicalCounter + docList.Count, nodeId);

                var oplog = new OplogEntry(_name, key, OperationType.Delete, null, timestamp, previousHash ?? string.Empty);
                previousHash = oplog.Hash;

                var paramsContainer = new Document(_name, key, empty, timestamp, true);

                docList.Add(paramsContainer);
                oplogList.Add(oplog);
            }

            _db.LastHash = previousHash;
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
