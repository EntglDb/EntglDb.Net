using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BLite.Bson;
using BLite.Core;
using BLite.Core.Collections;
using BLite.Core.Indexing;
using BLite.Core.Storage;
using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Persistence.Blite.Internal;

namespace EntglDb.Persistence.Blite;

public class BlitePeerStore : IPeerStore, IDisposable
{
    private readonly StorageEngine _storage;
    private readonly ConcurrentDictionary<string, DocumentCollection<string, Document>> _collections = new();
    private readonly DocumentCollection<string, OplogEntry> _oplog;
    private readonly DocumentCollection<string, RemotePeerConfiguration> _remotePeers;
    private readonly string _databasePath;

    public event EventHandler<ChangesAppliedEventArgs>? ChangesApplied;

    public BlitePeerStore(string databasePath)
    {
        _databasePath = databasePath;
        _storage = new StorageEngine(databasePath, PageFileConfig.Default);
        
        // Initialize Oplog collection
        var oplogMapper = new OplogMapper(_storage);
        _oplog = new DocumentCollection<string, OplogEntry>(_storage, oplogMapper, "_oplog");
        
        // Initialize Remote Peers collection
        var peerMapper = new RemotePeerConfigurationMapper(_storage);
        _remotePeers = new DocumentCollection<string, RemotePeerConfiguration>(_storage, peerMapper, "_remote_peers");

        _storage.RegisterMappers(new IDocumentMapper[] { oplogMapper, peerMapper });
    }

    private DocumentCollection<string, Document> GetCollection(string name)
    {
        return _collections.GetOrAdd(name, n =>
        {
            var mapper = new DocumentMapper(_storage, n);
            var collection = new DocumentCollection<string, Document>(_storage, mapper, n);
            _storage.RegisterMappers(new[] { mapper });
            return collection;
        });
    }

    public async Task SaveDocumentAsync(Document document, CancellationToken cancellationToken = default)
    {
        var coll = GetCollection(document.Collection);
        if (coll.FindById(document.Key) == null)
            await coll.InsertAsync(document);
        else
            await coll.UpdateAsync(document);
    }

    public async Task<Document?> GetDocumentAsync(string collection, string key, CancellationToken cancellationToken = default)
    {
        var coll = GetCollection(collection);
        return await Task.FromResult(coll.FindById(key));
    }

    public async Task AppendOplogEntryAsync(OplogEntry entry, CancellationToken cancellationToken = default)
    {
        if (_oplog.FindById(entry.Hash) == null)
            await _oplog.InsertAsync(entry);
        else
            await _oplog.UpdateAsync(entry);
    }

    public async Task<IEnumerable<OplogEntry>> GetOplogAfterAsync(HlcTimestamp timestamp, CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(_oplog.AsQueryable()
            .Where(e => e.Timestamp > timestamp)
            .ToList());
    }

    public async Task<HlcTimestamp> GetLatestTimestampAsync(CancellationToken cancellationToken = default)
    {
        var latest = _oplog.AsQueryable()
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefault();

        return await Task.FromResult(latest?.Timestamp ?? default);
    }

    public async Task<VectorClock> GetVectorClockAsync(CancellationToken cancellationToken = default)
    {
        var clock = new VectorClock();
        var entries = _oplog.AsQueryable().ToList();
        
        foreach (var entry in entries)
        {
            clock.SetTimestamp(entry.Timestamp.NodeId, entry.Timestamp);
        }

        return await Task.FromResult(clock);
    }

    public async Task<IEnumerable<OplogEntry>> GetOplogForNodeAfterAsync(string nodeId, HlcTimestamp since, CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(_oplog.AsQueryable()
            .Where(e => e.Timestamp.NodeId == nodeId && e.Timestamp > since)
            .ToList());
    }

    public async Task<string?> GetLastEntryHashAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        var last = _oplog.AsQueryable()
            .Where(e => e.Timestamp.NodeId == nodeId)
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefault();

        return await Task.FromResult(last?.Hash);
    }

    public async Task<IEnumerable<OplogEntry>> GetChainRangeAsync(string startHash, string endHash, CancellationToken cancellationToken = default)
    {
        var all = _oplog.AsQueryable().ToList();
        var result = new List<OplogEntry>();
        
        var current = all.FirstOrDefault(e => e.Hash == endHash);
        while (current != null && current.Hash != startHash)
        {
            result.Insert(0, current);
            current = all.FirstOrDefault(e => e.Hash == current.PreviousHash);
        }

        return await Task.FromResult(result);
    }

    public async Task<OplogEntry?> GetEntryByHashAsync(string hash, CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(_oplog.FindById(hash));
    }

    public async Task ApplyBatchAsync(IEnumerable<Document> documents, IEnumerable<OplogEntry> oplogEntries, CancellationToken cancellationToken = default)
    {
        // Warm up collections outside transaction to ensure mappers and keys are registered
        foreach (var doc in documents)
        {
            GetCollection(doc.Collection);
        }

        using var txn = await _storage.BeginTransactionAsync(ct: cancellationToken);
        try
        {
            foreach (var doc in documents)
            {
                var coll = GetCollection(doc.Collection);
                if (coll.FindById(doc.Key, txn) == null)
                    coll.Insert(doc, txn);
                else
                    coll.Update(doc, txn);
            }

            foreach (var entry in oplogEntries)
            {
                if (_oplog.FindById(entry.Hash, txn) == null)
                    _oplog.Insert(entry, txn);
                else
                    _oplog.Update(entry, txn);
            }

            await txn.CommitAsync(cancellationToken);
            ChangesApplied?.Invoke(this, new ChangesAppliedEventArgs(oplogEntries));
        }
        catch
        {
            txn.Rollback();
            throw;
        }
    }

    public async Task<IEnumerable<Document>> QueryDocumentsAsync(string collection, QueryNode? queryExpression, int? skip = null, int? take = null, string? orderBy = null, bool ascending = true, CancellationToken cancellationToken = default)
    {
        var coll = GetCollection(collection);
        var query = QueryTranslator.Translate(collection, queryExpression);
        
        var results = coll.AsQueryable().Where(query);

        if (skip.HasValue) results = results.Skip(skip.Value);
        if (take.HasValue) results = results.Take(take.Value);

        return await Task.FromResult(results.ToList());
    }

    public async Task<int> CountDocumentsAsync(string collection, QueryNode? queryExpression, CancellationToken cancellationToken = default)
    {
        var coll = GetCollection(collection);
        var query = QueryTranslator.Translate(collection, queryExpression);
        return await Task.FromResult(coll.AsQueryable().Where(query).Count());
    }

    public async Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(_collections.Keys.AsEnumerable());
    }

    public async Task EnsureIndexAsync(string collection, string propertyPath, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
    }

    public async Task SaveRemotePeerAsync(RemotePeerConfiguration peer, CancellationToken cancellationToken = default)
    {
        if (_remotePeers.FindById(peer.NodeId) == null)
            await _remotePeers.InsertAsync(peer);
        else
            await _remotePeers.UpdateAsync(peer);
    }

    public async Task<IEnumerable<RemotePeerConfiguration>> GetRemotePeersAsync(CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(_remotePeers.AsQueryable().ToList());
    }

    public async Task RemoveRemotePeerAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        _remotePeers.Delete(nodeId);
        await Task.CompletedTask;
    }

    public async Task PruneOplogAsync(HlcTimestamp cutoff, CancellationToken cancellationToken = default)
    {
        var toDelete = _oplog.AsQueryable().Where(e => e.Timestamp < cutoff).ToList();
        foreach (var entry in toDelete)
        {
            _oplog.Delete(entry.Hash);
        }
        await Task.CompletedTask;
    }

    public async Task CreateSnapshotAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        using var fs = File.OpenRead(_databasePath);
        await fs.CopyToAsync(destination, cancellationToken);
    }

    public async Task ReplaceDatabaseAsync(Stream databaseStream, CancellationToken cancellationToken = default)
    {
        _storage.Dispose();
        using (var fs = File.Create(_databasePath))
        {
            await databaseStream.CopyToAsync(fs, cancellationToken);
        }
    }

    public async Task MergeSnapshotAsync(Stream snapshotStream, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public async Task<string?> GetSnapshotHashAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        return await Task.FromResult<string?>(null);
    }

    public async Task ClearAllDataAsync(CancellationToken cancellationToken = default)
    {
        _storage.Dispose();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        var walPath = Path.ChangeExtension(_databasePath, ".wal");
        if (File.Exists(walPath)) File.Delete(walPath);
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        _storage?.Dispose();
    }
}
