using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Persistence.EntityFramework.Entities;
using EntglDb.Persistence.Sqlite;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;

namespace EntglDb.Persistence.EntityFramework;

public class EfCoreOplogStore<TDbContext> : OplogStore where TDbContext : DbContext
{
    private class NodeLatestEntryResult
    {
        public string NodeId { get; set; } = default!;
        public OplogEntity? MaxEntry { get; set; }
    }

    protected readonly TDbContext _context;
    protected readonly ILogger<EfCoreOplogStore<TDbContext>> _logger;
    protected readonly ISnapshotMetadataStore _snapshotMetadataStore;

    public EfCoreOplogStore(
        TDbContext context,
        IDocumentStore documentStore,
        ISnapshotMetadataStore snapshotMetadataStore,
        IConflictResolver? conflictResolver = null,
        ILogger<EfCoreOplogStore<TDbContext>>? logger = null) : base(documentStore, conflictResolver, snapshotMetadataStore)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _snapshotMetadataStore = snapshotMetadataStore ?? throw new ArgumentNullException(nameof(snapshotMetadataStore));
        _logger = logger ?? NullLogger<EfCoreOplogStore<TDbContext>>.Instance;
        
        // Re-initialize cache now that _context is assigned
        // (base constructor called InitializeNodeCache but _context was null)
        _cacheInitialized = false;
        InitializeNodeCache();
    }

    /// <inheritdoc />
    protected override void InitializeNodeCache()
    {
        if (_cacheInitialized) return;

        // Early exit: protect against constructor initialization order
        // InitializeNodeCache is called from base constructor before _context is assigned
        // Don't mark as initialized - will be called again after _context is set
        if (_context == null)
        {
            return;
        }

        _cacheLock.Wait();
        try
        {
            if (_cacheInitialized) return;

            // Step 1: Load from SnapshotMetadata FIRST (base state after prune)
            if (_snapshotMetadataStore != null)
            {
                try
                {
                    var snapshots = _snapshotMetadataStore.GetAllSnapshotMetadataAsync().GetAwaiter().GetResult();
                    foreach (var snapshot in snapshots)
                    {
                        _nodeCache[snapshot.NodeId] = new NodeCacheEntry
                        {
                            Timestamp = new HlcTimestamp(
                                snapshot.TimestampPhysicalTime,
                                snapshot.TimestampLogicalCounter,
                                snapshot.NodeId),
                            Hash = snapshot.Hash ?? ""
                        };
                    }
                }
                catch
                {
                    // Ignore errors during initialization - oplog data will be used as fallback
                }
            }

            // Step 2: Load from Oplog (Latest State - Overrides Snapshot if newer)
            var latestPerNode = _context.Set<OplogEntity>()
                .GroupBy(o => o.TimestampNodeId)
                .Select(g => new NodeLatestEntryResult
                {
                    NodeId = g.Key,
                    MaxEntry = g.OrderByDescending(o => o.TimestampPhysicalTime)
                                .ThenByDescending(o => o.TimestampLogicalCounter)
                                .FirstOrDefault()
                })
                .ToList()
                .Where(x => x.MaxEntry != null)
                .ToList();

            foreach (var node in latestPerNode)
            {
                if (node.MaxEntry != null)
                {
                    var timestamp = new HlcTimestamp(
                        node.MaxEntry.TimestampPhysicalTime,
                        node.MaxEntry.TimestampLogicalCounter,
                        node.MaxEntry.TimestampNodeId);

                    // Only update if newer than snapshot
                    if (!_nodeCache.TryGetValue(node.NodeId, out var existing) || timestamp.CompareTo(existing.Timestamp) > 0)
                    {
                        _nodeCache[node.NodeId] = new NodeCacheEntry
                        {
                            Timestamp = timestamp,
                            Hash = node.MaxEntry.Hash ?? ""
                        };
                    }
                }
            }

            _cacheInitialized = true;
            _logger.LogInformation("Node cache initialized with {Count} nodes", _nodeCache.Count);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <inheritdoc />
    public override async Task ApplyBatchAsync(IEnumerable<OplogEntry> oplogEntries, CancellationToken cancellationToken = default)
    {
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var documentKeys = oplogEntries.Select(e => (e.Collection, e.Key)).Distinct().ToList();
            var documentsToFetch = await _documentStore.GetDocumentsAsync(documentKeys, cancellationToken);

            var orderdedEntriesPerCollectionKey = oplogEntries
                .GroupBy(e => (e.Collection, e.Key))
                .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Timestamp.PhysicalTime)
                                                 .ThenBy(e => e.Timestamp.LogicalCounter)
                                                 .ToList());

            foreach (var entry in orderdedEntriesPerCollectionKey)
            {
                var document = documentsToFetch.FirstOrDefault(d => d.Collection == entry.Key.Collection && d.Key == entry.Key.Key);

                if (entry.Value.Any(v => v.Operation == OperationType.Delete))
                {
                    if (document != null)
                    {
                        await _documentStore.DeleteDocumentAsync(entry.Key.Collection, entry.Key.Key, cancellationToken);
                    }
                    continue;
                }

                var documentHash = document != null ? document.GetHashCode().ToString() : null;

                foreach (var oplogEntry in entry.Value)
                {
                    if (document == null && (oplogEntry.Operation == OperationType.Put) && oplogEntry.Payload != null)
                    {
                        document = new Document(oplogEntry.Collection, oplogEntry.Key, oplogEntry.Payload!.Value, oplogEntry.Timestamp, false);
                    }
                    else
                    {
                        document?.Merge(oplogEntry, _conflictResolver);                        
                    }
                }

                if(document?.GetHashCode().ToString() != documentHash)
                {
                    await _documentStore.PutDocumentAsync(document!, cancellationToken);
                }
            }

            //insert all oplog entries after processing documents to ensure oplog reflects the actual state of documents
            await _context.Set<OplogEntity>().AddRangeAsync(oplogEntries.Select(entry => new OplogEntity
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
            }), cancellationToken);

            _nodeCache?.Clear();
            OnChangesApplied(oplogEntries);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task<IEnumerable<OplogEntry>> GetChainRangeAsync(string startHash, string endHash, CancellationToken cancellationToken = default)
    {
        // 1. Fetch bounds to identify the chain and range
        var startRow = await _context.Set<OplogEntity>()
            .Where(o => o.Hash == startHash)
            .Select(o => new { o.TimestampPhysicalTime, o.TimestampLogicalCounter, o.TimestampNodeId })
            .FirstOrDefaultAsync(cancellationToken);

        var endRow = await _context.Set<OplogEntity>()
            .Where(o => o.Hash == endHash)
            .Select(o => new { o.TimestampPhysicalTime, o.TimestampLogicalCounter, o.TimestampNodeId })
            .FirstOrDefaultAsync(cancellationToken);

        if (startRow == null || endRow == null) return [];
        if (startRow.TimestampNodeId != endRow.TimestampNodeId) return [];

        var nodeId = startRow.TimestampNodeId;

        // 2. Fetch range (Start < Entry <= End)
        var entities = await _context.Set<OplogEntity>()
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

    /// <inheritdoc />
    public override async Task<OplogEntry?> GetEntryByHashAsync(string hash, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Set<OplogEntity>()
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

    /// <inheritdoc />
    public override async Task<IEnumerable<OplogEntry>> GetOplogAfterAsync(HlcTimestamp timestamp, IEnumerable<string>? collections = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Set<OplogEntity>()
            .Where(o => o.TimestampPhysicalTime > timestamp.PhysicalTime ||
                       (o.TimestampPhysicalTime == timestamp.PhysicalTime &&
                        o.TimestampLogicalCounter > timestamp.LogicalCounter));

        if (collections != null && collections.Any())
        {
            query = query.Where(o => collections.Contains(o.Collection));
        }

        var entities = await query
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

    /// <inheritdoc />
    public override async Task<IEnumerable<OplogEntry>> GetOplogForNodeAfterAsync(string nodeId, HlcTimestamp since, IEnumerable<string>? collections = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Set<OplogEntity>()
            .Where(o => o.TimestampNodeId == nodeId &&
                       ((o.TimestampPhysicalTime > since.PhysicalTime) ||
                        (o.TimestampPhysicalTime == since.PhysicalTime && o.TimestampLogicalCounter > since.LogicalCounter)));

        if (collections != null && collections.Any())
        {
            query = query.Where(o => collections.Contains(o.Collection));
        }

        var entities = await query
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

    public override async Task PruneOplogAsync(HlcTimestamp cutoff, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Pruning oplog entries older than {Cutoff}...", cutoff);

        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Find entries to delete
            var entriesToDelete = await _context.Set<OplogEntity>()
                .Where(o => o.TimestampPhysicalTime < cutoff.PhysicalTime ||
                           (o.TimestampPhysicalTime == cutoff.PhysicalTime && o.TimestampLogicalCounter < cutoff.LogicalCounter))
                .ToListAsync(cancellationToken);

            if (!entriesToDelete.Any())
            {
                _logger.LogInformation("No oplog entries to prune.");
                return;
            }

            // Update SnapshotMetadata with boundary entries (latest before cutoff per node)
            var boundaryEntries = entriesToDelete
                .GroupBy(o => o.TimestampNodeId)
                .Select(g => g.OrderByDescending(o => o.TimestampPhysicalTime)
                              .ThenByDescending(o => o.TimestampLogicalCounter)
                              .First())
                .ToList();

            foreach (var entry in boundaryEntries)
            {

                var existingMeta = await _snapshotMetadataStore.GetSnapshotMetadataAsync(entry.TimestampNodeId, cancellationToken);

                if (existingMeta == null)
                {
                    await _snapshotMetadataStore.InsertSnapshotMetadataAsync(new SnapshotMetadata
                    {
                        NodeId = entry.TimestampNodeId,
                        TimestampPhysicalTime = entry.TimestampPhysicalTime,
                        TimestampLogicalCounter = entry.TimestampLogicalCounter,
                        Hash = entry.Hash ?? ""
                    });
                }
                else
                {
                    existingMeta.TimestampPhysicalTime = entry.TimestampPhysicalTime;
                    existingMeta.TimestampLogicalCounter = entry.TimestampLogicalCounter;
                    existingMeta.Hash = entry.Hash ?? "";
                    await _snapshotMetadataStore.UpdateSnapshotMetadataAsync(existingMeta, cancellationToken);
                }
            }

            // Delete old entries
            _context.Set<OplogEntity>().RemoveRange(entriesToDelete);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("Pruned {Count} oplog entries.", entriesToDelete.Count);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async Task InsertOplogEntryAsync(OplogEntry entry, CancellationToken cancellationToken = default)
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

        _context.Set<OplogEntity>().Add(entity);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task<string?> QueryLastHashForNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        // Cache miss - query database
        var latest = await _context.Set<OplogEntity>()
            .Where(o => o.TimestampNodeId == nodeId)
            .OrderByDescending(o => o.TimestampPhysicalTime)
            .ThenByDescending(o => o.TimestampLogicalCounter)
            .Select(o => new { o.Hash, o.TimestampPhysicalTime, o.TimestampLogicalCounter })
            .FirstOrDefaultAsync(cancellationToken);

        return latest?.Hash;
    }

    /// <inheritdoc />
    protected override async Task<(long Wall, int Logic)?> QueryLastHashTimestampFromOplogAsync(string hash, CancellationToken cancellationToken = default)
    {
        var entry = await _context.Set<OplogEntity>()
            .Where(o => o.Hash == hash)
            .Select(o => new { o.TimestampPhysicalTime, o.TimestampLogicalCounter })
            .FirstOrDefaultAsync(cancellationToken);
        if (entry == null) return null;
        return (entry.TimestampPhysicalTime, entry.TimestampLogicalCounter);
    }

    /// <inheritdoc />
    public override async Task DropAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Dropping oplog store - all oplog entries and snapshots will be permanently deleted!");
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _context.Set<OplogEntity>().RemoveRange(_context.Set<OplogEntity>());
            _context.Set<SnapshotMetadataEntity>().RemoveRange(_context.Set<SnapshotMetadataEntity>());
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _nodeCache.Clear();
            _logger.LogInformation("Oplog store dropped successfully.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError("Failed to drop oplog store.");
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task<IEnumerable<OplogEntry>> ExportAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _context.Set<OplogEntity>()
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

    /// <inheritdoc />
    public override async Task ImportAsync(IEnumerable<OplogEntry> items, CancellationToken cancellationToken = default)
    {
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var entry in items)
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
                _context.Set<OplogEntity>().Add(entity);
            }
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task MergeAsync(IEnumerable<OplogEntry> items, CancellationToken cancellationToken = default)
    {
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var entry in items)
            {
                var existing = await _context.Set<OplogEntity>()
                    .FirstOrDefaultAsync(o => o.Hash == entry.Hash, cancellationToken);
                if (existing == null)
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
                    _context.Set<OplogEntity>().Add(entity);
                }
            }
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
