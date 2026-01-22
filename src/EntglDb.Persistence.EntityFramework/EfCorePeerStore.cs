using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Core.Network;
using EntglDb.Persistence.EntityFramework.Entities;

namespace EntglDb.Persistence.EntityFramework;

/// <summary>
/// Entity Framework Core implementation of <see cref="IPeerStore"/>.
/// Supports SQL Server, PostgreSQL, MySQL, and SQLite via EF Core.
/// </summary>
public class EfCorePeerStore : IPeerStore
{
    protected readonly EntglDbContext _context;
    protected readonly ILogger<EfCorePeerStore> _logger;
    protected readonly IConflictResolver _conflictResolver;
    
    // Per-node cache: tracks latest timestamp and hash for each node
    private readonly object _cacheLock = new object();
    private readonly Dictionary<string, NodeCacheEntry> _nodeCache = new Dictionary<string, NodeCacheEntry>(StringComparer.Ordinal);
    private bool _cacheInitialized = false;
    
    private class NodeCacheEntry
    {
        public HlcTimestamp Timestamp { get; set; }
        public string Hash { get; set; } = "";
    }

    public event EventHandler<ChangesAppliedEventArgs>? ChangesApplied;

    /// <summary>
    /// Initializes a new instance of the <see cref="EfCorePeerStore"/> class.
    /// </summary>
    public EfCorePeerStore(
        EntglDbContext context,
        ILogger<EfCorePeerStore>? logger = null,
        IConflictResolver? conflictResolver = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? NullLogger<EfCorePeerStore>.Instance;
        _conflictResolver = conflictResolver ?? new LastWriteWinsConflictResolver();
    }

    private async Task EnsureCacheInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_cacheInitialized) return;

        lock (_cacheLock)
        {
            if (_cacheInitialized) return;

            // Query latest entry per node
            var latestPerNode = _context.Oplog
                .GroupBy(o => o.TimestampNodeId)
                .Select(g => new
                {
                    NodeId = g.Key,
                    MaxEntry = g.OrderByDescending(o => o.TimestampPhysicalTime)
                                .ThenByDescending(o => o.TimestampLogicalCounter)
                                .FirstOrDefault()
                })
                .Where(x => x.MaxEntry != null)
                .ToList();

            _nodeCache.Clear();
            foreach (var node in latestPerNode)
            {
                if (node.MaxEntry != null)
                {
                    _nodeCache[node.NodeId] = new NodeCacheEntry
                    {
                        Timestamp = new HlcTimestamp(
                            node.MaxEntry.TimestampPhysicalTime,
                            node.MaxEntry.TimestampLogicalCounter,
                            node.MaxEntry.TimestampNodeId),
                        Hash = node.MaxEntry.Hash ?? ""
                    };
                }
            }

            _cacheInitialized = true;
            _logger.LogInformation("Node cache initialized with {Count} nodes", _nodeCache.Count);
        }
    }

    public async Task SaveDocumentAsync(Document document, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Documents
            .FirstOrDefaultAsync(d => d.Collection == document.Collection && d.Key == document.Key, cancellationToken);

        if (entity == null)
        {
            entity = new DocumentEntity
            {
                Collection = document.Collection,
                Key = document.Key
            };
            _context.Documents.Add(entity);
        }

        entity.ContentJson = document.Content.ValueKind == JsonValueKind.Undefined 
            ? "{}" 
            : document.Content.GetRawText();
        entity.IsDeleted = document.IsDeleted;
        entity.UpdatedAtPhysicalTime = document.UpdatedAt.PhysicalTime;
        entity.UpdatedAtLogicalCounter = document.UpdatedAt.LogicalCounter;
        entity.UpdatedAtNodeId = document.UpdatedAt.NodeId;

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Document?> GetDocumentAsync(string collection, string key, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Documents
            .FirstOrDefaultAsync(d => d.Collection == collection && d.Key == key, cancellationToken);

        if (entity == null)
            return null;

        var hlc = new HlcTimestamp(
            entity.UpdatedAtPhysicalTime,
            entity.UpdatedAtLogicalCounter,
            entity.UpdatedAtNodeId);

        var content = string.IsNullOrEmpty(entity.ContentJson)
            ? default
            : JsonSerializer.Deserialize<JsonElement>(entity.ContentJson);

        return new Document(collection, key, content, hlc, entity.IsDeleted);
    }

    public async Task AppendOplogEntryAsync(OplogEntry entry, CancellationToken cancellationToken = default)
    {
        var entity = new OplogEntity
        {
            Collection = entry.Collection,
            Key = entry.Key,
            Operation = (int)entry.Operation,
            PayloadJson = entry.Payload?.GetRawText(),
            TimestampPhysicalTime = entry.Timestamp.PhysicalTime,
            TimestampLogicalCounter = entry.Timestamp.LogicalCounter,
            TimestampNodeId = entry.Timestamp.NodeId,
            Hash = entry.Hash,
            PreviousHash = entry.PreviousHash
        };

        _context.Oplog.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        // Update cache
        lock (_cacheLock)
        {
            var nodeId = entry.Timestamp.NodeId;
            if (!_nodeCache.TryGetValue(nodeId, out var existing) || entry.Timestamp.CompareTo(existing.Timestamp) > 0)
            {
                _nodeCache[nodeId] = new NodeCacheEntry
                {
                    Timestamp = entry.Timestamp,
                    Hash = entry.Hash ?? ""
                };
            }
        }
    }

    public async Task<IEnumerable<OplogEntry>> GetOplogAfterAsync(HlcTimestamp timestamp, CancellationToken cancellationToken = default)
    {
        var entities = await _context.Oplog
            .Where(o => o.TimestampPhysicalTime > timestamp.PhysicalTime ||
                       (o.TimestampPhysicalTime == timestamp.PhysicalTime &&
                        o.TimestampLogicalCounter > timestamp.LogicalCounter))
            .OrderBy(o => o.TimestampPhysicalTime)
            .ThenBy(o => o.TimestampLogicalCounter)
            .ToListAsync(cancellationToken);

        return entities.Select(e => new OplogEntry(
            e.Collection,
            e.Key,
            (OperationType)e.Operation,
            string.IsNullOrEmpty(e.PayloadJson) ? null : JsonSerializer.Deserialize<JsonElement>(e.PayloadJson),
            new HlcTimestamp(e.TimestampPhysicalTime, e.TimestampLogicalCounter, e.TimestampNodeId),
            e.PreviousHash ?? "",
            e.Hash
        ));
    }

    public async Task<HlcTimestamp> GetLatestTimestampAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCacheInitializedAsync(cancellationToken);

        lock (_cacheLock)
        {
            if (_nodeCache.Count == 0)
            {
                return new HlcTimestamp(0, 0, "");
            }

            var maxTimestamp = _nodeCache.Values
                .Select(e => e.Timestamp)
                .OrderByDescending(t => t)
                .First();

            return maxTimestamp;
        }
    }

    public async Task<VectorClock> GetVectorClockAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCacheInitializedAsync(cancellationToken);

        lock (_cacheLock)
        {
            var vectorClock = new VectorClock();
            foreach (var kvp in _nodeCache)
            {
                vectorClock.SetTimestamp(kvp.Key, kvp.Value.Timestamp);
            }
            return vectorClock;
        }
    }

    public async Task<IEnumerable<OplogEntry>> GetOplogForNodeAfterAsync(string nodeId, HlcTimestamp since, CancellationToken cancellationToken = default)
    {
        var entities = await _context.Oplog
            .Where(o => o.TimestampNodeId == nodeId &&
                       ((o.TimestampPhysicalTime > since.PhysicalTime) ||
                        (o.TimestampPhysicalTime == since.PhysicalTime && o.TimestampLogicalCounter > since.LogicalCounter)))
            .OrderBy(o => o.TimestampPhysicalTime)
            .ThenBy(o => o.TimestampLogicalCounter)
            .ToListAsync(cancellationToken);

        return entities.Select(e => new OplogEntry(
            e.Collection,
            e.Key,
            (OperationType)e.Operation,
            string.IsNullOrEmpty(e.PayloadJson) ? null : JsonSerializer.Deserialize<JsonElement>(e.PayloadJson),
            new HlcTimestamp(e.TimestampPhysicalTime, e.TimestampLogicalCounter, e.TimestampNodeId),
            e.PreviousHash ?? "",
            e.Hash
        ));
    }

    public async Task<string?> GetLastEntryHashAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        await EnsureCacheInitializedAsync(cancellationToken);

        // Try cache first
        lock (_cacheLock)
        {
            if (_nodeCache.TryGetValue(nodeId, out var entry))
            {
                return entry.Hash;
            }
        }

        // Cache miss - query database
        var latest = await _context.Oplog
            .Where(o => o.TimestampNodeId == nodeId)
            .OrderByDescending(o => o.TimestampPhysicalTime)
            .ThenByDescending(o => o.TimestampLogicalCounter)
            .Select(o => new { o.Hash, o.TimestampPhysicalTime, o.TimestampLogicalCounter })
            .FirstOrDefaultAsync(cancellationToken);

        if (latest != null)
        {
            // Update cache
            lock (_cacheLock)
            {
                _nodeCache[nodeId] = new NodeCacheEntry
                {
                    Timestamp = new HlcTimestamp(latest.TimestampPhysicalTime, latest.TimestampLogicalCounter, nodeId),
                    Hash = latest.Hash ?? ""
                };
            }
        }
            
        return latest?.Hash;
    }

    public async Task<OplogEntry?> GetEntryByHashAsync(string hash, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Oplog
            .FirstOrDefaultAsync(o => o.Hash == hash, cancellationToken);
            
        if (entity == null) return null;
        
        return new OplogEntry(
            entity.Collection,
            entity.Key,
            (OperationType)entity.Operation,
            string.IsNullOrEmpty(entity.PayloadJson) ? null : JsonSerializer.Deserialize<JsonElement>(entity.PayloadJson),
            new HlcTimestamp(entity.TimestampPhysicalTime, entity.TimestampLogicalCounter, entity.TimestampNodeId),
            entity.PreviousHash ?? "",
            entity.Hash
        );
    }

    public async Task<IEnumerable<OplogEntry>> GetChainRangeAsync(string startHash, string endHash, CancellationToken cancellationToken = default)
    {
        // 1. Fetch bounds to identify the chain and range
        var startRow = await _context.Oplog
            .Where(o => o.Hash == startHash)
            .Select(o => new { o.TimestampPhysicalTime, o.TimestampLogicalCounter, o.TimestampNodeId })
            .FirstOrDefaultAsync(cancellationToken);

        var endRow = await _context.Oplog
            .Where(o => o.Hash == endHash)
            .Select(o => new { o.TimestampPhysicalTime, o.TimestampLogicalCounter, o.TimestampNodeId })
            .FirstOrDefaultAsync(cancellationToken);

        if (startRow == null || endRow == null) return Enumerable.Empty<OplogEntry>();
        if (startRow.TimestampNodeId != endRow.TimestampNodeId) return Enumerable.Empty<OplogEntry>();
        
        var nodeId = startRow.TimestampNodeId;

        // 2. Fetch range (Start < Entry <= End)
        var entities = await _context.Oplog
            .Where(o => o.TimestampNodeId == nodeId &&
                       ((o.TimestampPhysicalTime > startRow.TimestampPhysicalTime) ||
                        (o.TimestampPhysicalTime == startRow.TimestampPhysicalTime && o.TimestampLogicalCounter > startRow.TimestampLogicalCounter)) &&
                       ((o.TimestampPhysicalTime < endRow.TimestampPhysicalTime) ||
                        (o.TimestampPhysicalTime == endRow.TimestampPhysicalTime && o.TimestampLogicalCounter <= endRow.TimestampLogicalCounter)))
            .OrderBy(o => o.TimestampPhysicalTime)
            .ThenBy(o => o.TimestampLogicalCounter)
            .ToListAsync(cancellationToken);

        return entities.Select(e => new OplogEntry(
            e.Collection,
            e.Key,
            (OperationType)e.Operation,
            string.IsNullOrEmpty(e.PayloadJson) ? null : JsonSerializer.Deserialize<JsonElement>(e.PayloadJson),
            new HlcTimestamp(e.TimestampPhysicalTime, e.TimestampLogicalCounter, e.TimestampNodeId),
            e.PreviousHash ?? "",
            e.Hash
        ));
    }

    public async Task ApplyBatchAsync(IEnumerable<Document> documents, IEnumerable<OplogEntry> oplogEntries, CancellationToken cancellationToken = default)
    {
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var entry in oplogEntries)
            {
                var localEntity = await _context.Documents
                    .FirstOrDefaultAsync(d => d.Collection == entry.Collection && d.Key == entry.Key, cancellationToken);

                Document? localDoc = null;
                if (localEntity != null)
                {
                    var localHlc = new HlcTimestamp(
                        localEntity.UpdatedAtPhysicalTime,
                        localEntity.UpdatedAtLogicalCounter,
                        localEntity.UpdatedAtNodeId);

                    var localContent = string.IsNullOrEmpty(localEntity.ContentJson)
                        ? default
                        : JsonSerializer.Deserialize<JsonElement>(localEntity.ContentJson);

                    localDoc = new Document(entry.Collection, entry.Key, localContent, localHlc, localEntity.IsDeleted);
                }

                var resolution = _conflictResolver.Resolve(localDoc, entry);

                if (resolution.ShouldApply && resolution.MergedDocument != null)
                {
                    var doc = resolution.MergedDocument;

                    if (localEntity == null)
                    {
                        localEntity = new DocumentEntity
                        {
                            Collection = doc.Collection,
                            Key = doc.Key
                        };
                        _context.Documents.Add(localEntity);
                    }

                    localEntity.ContentJson = doc.Content.ValueKind == JsonValueKind.Undefined
                        ? "{}"
                        : doc.Content.GetRawText();
                    localEntity.IsDeleted = doc.IsDeleted;
                    localEntity.UpdatedAtPhysicalTime = doc.UpdatedAt.PhysicalTime;
                    localEntity.UpdatedAtLogicalCounter = doc.UpdatedAt.LogicalCounter;
                    localEntity.UpdatedAtNodeId = doc.UpdatedAt.NodeId;
                }

                var oplogEntity = new OplogEntity
                {
                    Collection = entry.Collection,
                    Key = entry.Key,
                    Operation = (int)entry.Operation,
                    PayloadJson = entry.Payload?.GetRawText(),
                    TimestampPhysicalTime = entry.Timestamp.PhysicalTime,
                    TimestampLogicalCounter = entry.Timestamp.LogicalCounter,
                    TimestampNodeId = entry.Timestamp.NodeId,
                    Hash = entry.Hash,
                    PreviousHash = entry.PreviousHash
                };
                _context.Oplog.Add(oplogEntity);
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            try
            {
                ChangesApplied?.Invoke(this, new ChangesAppliedEventArgs(oplogEntries));

                // Update cache for all entries
                lock (_cacheLock)
                {
                    foreach (var entry in oplogEntries)
                    {
                        var nodeId = entry.Timestamp.NodeId;
                        if (!_nodeCache.TryGetValue(nodeId, out var existing) || entry.Timestamp.CompareTo(existing.Timestamp) > 0)
                        {
                            _nodeCache[nodeId] = new NodeCacheEntry
                            {
                                Timestamp = entry.Timestamp,
                                Hash = entry.Hash ?? ""
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling ChangesApplied event or updating cache");
            }
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IEnumerable<Document>> QueryDocumentsAsync(
        string collection,
        QueryNode? queryExpression,
        int? skip = null,
        int? take = null,
        string? orderBy = null,
        bool ascending = true,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Documents
            .Where(d => d.Collection == collection && !d.IsDeleted);

        if (queryExpression != null)
        {
            query = ApplyQueryExpression(query, queryExpression);
        }

        if (!string.IsNullOrEmpty(orderBy))
        {
            // Basic ordering by key for now - full JSON path ordering would require database-specific functions
            query = ascending
                ? query.OrderBy(d => d.Key)
                : query.OrderByDescending(d => d.Key);
        }
        else
        {
            query = query.OrderBy(d => d.Key);
        }

        if (skip.HasValue)
            query = query.Skip(skip.Value);

        if (take.HasValue)
            query = query.Take(take.Value);

        var entities = await query.ToListAsync(cancellationToken);

        return entities.Select(e =>
        {
            var hlc = new HlcTimestamp(
                e.UpdatedAtPhysicalTime,
                e.UpdatedAtLogicalCounter,
                e.UpdatedAtNodeId);

            var content = string.IsNullOrEmpty(e.ContentJson)
                ? default
                : JsonSerializer.Deserialize<JsonElement>(e.ContentJson);

            return new Document(collection, e.Key, content, hlc, e.IsDeleted);
        });
    }

    protected virtual IQueryable<DocumentEntity> ApplyQueryExpression(IQueryable<DocumentEntity> query, QueryNode queryExpression)
    {
        // Basic implementation - filters in memory after retrieval
        // Derived classes (like PostgreSQL) can override for database-level filtering with JSONB
        _logger.LogWarning("Query expressions are evaluated in-memory for EF Core. Consider using PostgreSQL provider for efficient JSON queries.");
        return query;
    }

    public async Task<int> CountDocumentsAsync(string collection, QueryNode? queryExpression, CancellationToken cancellationToken = default)
    {
        var query = _context.Documents
            .Where(d => d.Collection == collection && !d.IsDeleted);

        if (queryExpression != null)
        {
            query = ApplyQueryExpression(query, queryExpression);
        }

        return await query.CountAsync(cancellationToken);
    }

    public async Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Documents
            .Select(d => d.Collection)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);
    }

    public async Task EnsureIndexAsync(string collection, string propertyPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Index creation for collection {Collection} on property {PropertyPath} is managed by migrations", collection, propertyPath);
        await Task.CompletedTask;
    }

    public async Task SaveRemotePeerAsync(RemotePeerConfiguration peer, CancellationToken cancellationToken = default)
    {
        var entity = await _context.RemotePeers
            .FirstOrDefaultAsync(p => p.NodeId == peer.NodeId, cancellationToken);

        if (entity == null)
        {
            entity = new RemotePeerEntity
            {
                NodeId = peer.NodeId
            };
            _context.RemotePeers.Add(entity);
        }

        entity.Address = peer.Address;
        entity.Type = (int)peer.Type;
        entity.OAuth2Json = peer.OAuth2Json;
        entity.IsEnabled = peer.IsEnabled;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Saved remote peer configuration: {NodeId} ({Type})", peer.NodeId, peer.Type);
    }

    public async Task<IEnumerable<RemotePeerConfiguration>> GetRemotePeersAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _context.RemotePeers.ToListAsync(cancellationToken);

        return entities.Select(e => new RemotePeerConfiguration
        {
            NodeId = e.NodeId,
            Address = e.Address,
            Type = (PeerType)e.Type,
            OAuth2Json = e.OAuth2Json,
            IsEnabled = e.IsEnabled
        });
    }

    public async Task RemoveRemotePeerAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.RemotePeers
            .FirstOrDefaultAsync(p => p.NodeId == nodeId, cancellationToken);

        if (entity != null)
        {
            _context.RemotePeers.Remove(entity);
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Removed remote peer configuration: {NodeId}", nodeId);
        }
        else
        {
            _logger.LogWarning("Attempted to remove non-existent remote peer: {NodeId}", nodeId);
        }
    }
}
