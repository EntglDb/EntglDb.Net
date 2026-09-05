using BLite.Core.Query;
using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Persistence.BLite.Entities;
using EntglDb.Persistence.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Persistence.BLite;

public class BLiteOplogStore : OplogStore
{
    protected readonly EntglDbMetaContext _context;
    protected readonly ILogger<BLiteOplogStore> _logger;

    public BLiteOplogStore(
        EntglDbMetaContext dbContext, 
        IDocumentStore documentStore, 
        IConflictResolver conflictResolver,
        IVectorClockService vectorClockService,
        ISnapshotMetadataStore? snapshotMetadataStore = null,
        ILogger<BLiteOplogStore>? logger = null) : base(documentStore, conflictResolver, vectorClockService, snapshotMetadataStore)
    {
        _context = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? NullLogger<BLiteOplogStore>.Instance;
        InitializeVectorClock();
    }

    public override async Task ApplyBatchAsync(IEnumerable<OplogEntry> oplogEntries, CancellationToken cancellationToken = default)
    {
        // BLite transactions are committed by each SaveChangesAsync internally.
        // Wrapping in an explicit transaction causes "Cannot rollback committed transaction"
        // because PutDocumentAsync → SaveChangesAsync already commits.
        await base.ApplyBatchAsync(oplogEntries, cancellationToken);
    }

    public override async Task DropAsync(CancellationToken cancellationToken = default)
    {
        // Use Id (technical key) for deletion, not Hash (business key)
        await _context.OplogEntries.DeleteBulkAsync((await _context.OplogEntries.FindAllAsync().ToListAsync()).Select(e => e.Id), cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        _vectorClock.Invalidate();
    }

    public override async Task<IEnumerable<OplogEntry>> ExportAsync(CancellationToken cancellationToken = default)
    {
        return (await _context.OplogEntries.FindAllAsync().ToListAsync()).ToDomain();
    }

    public override async Task<IEnumerable<OplogEntry>> GetChainRangeAsync(string startHash, string endHash, CancellationToken cancellationToken = default)
    {
        var startRow = await _context.OplogEntries.FindByIdAsync(startHash, cancellationToken);
        var endRow = await _context.OplogEntries.FindByIdAsync(endHash, cancellationToken);

        if (startRow == null || endRow == null) return [];

        var nodeId = startRow.TimestampNodeId;

        // 2. Fetch range (Start < Entry <= End)
        return await _context.OplogEntries
            .AsQueryable()
            .Where(o => o.TimestampNodeId == nodeId &&
                       ((o.TimestampPhysicalTime > startRow.TimestampPhysicalTime) ||
                        (o.TimestampPhysicalTime == startRow.TimestampPhysicalTime && o.TimestampLogicalCounter > startRow.TimestampLogicalCounter)) &&
                       ((o.TimestampPhysicalTime < endRow.TimestampPhysicalTime) ||
                        (o.TimestampPhysicalTime == endRow.TimestampPhysicalTime && o.TimestampLogicalCounter <= endRow.TimestampLogicalCounter)))
            .OrderBy(o => o.TimestampPhysicalTime)
            .ThenBy(o => o.TimestampLogicalCounter)
            .Select(e => e.ToDomain())
            .ToListAsync(cancellationToken);
    }

    public override async Task<OplogEntry?> GetEntryByHashAsync(string hash, CancellationToken cancellationToken = default)
    {
        // Hash is now a regular indexed property, not the Key
        return await _context.OplogEntries.AsQueryable()
            .Where(o => o.Hash == hash)
            .Select(o => o.ToDomain())
            .FirstOrDefaultAsync(cancellationToken);
    }

    public override async Task<IEnumerable<OplogEntry>> GetOplogAfterAsync(HlcTimestamp timestamp, IEnumerable<string>? collections = null, CancellationToken cancellationToken = default)
    {
        var query = _context.OplogEntries
            .AsQueryable()
            .Where(o => (o.TimestampPhysicalTime > timestamp.PhysicalTime) ||
                       (o.TimestampPhysicalTime == timestamp.PhysicalTime && o.TimestampLogicalCounter > timestamp.LogicalCounter));
        if (collections != null)
        {
            var collectionSet = new HashSet<string>(collections);
            query = query.Where(o => collectionSet.Contains(o.Collection));
        }
        return await query
            .OrderBy(o => o.TimestampPhysicalTime)
            .ThenBy(o => o.TimestampLogicalCounter)
            .Select(e => e.ToDomain())
            .ToListAsync(cancellationToken);
    }

    public override async Task<IEnumerable<OplogEntry>> GetOplogForNodeAfterAsync(string nodeId, HlcTimestamp since, IEnumerable<string>? collections = null, CancellationToken cancellationToken = default)
    {
        var query = _context.OplogEntries.AsQueryable()
            .Where(o => o.TimestampNodeId == nodeId &&
                       ((o.TimestampPhysicalTime > since.PhysicalTime) ||
                        (o.TimestampPhysicalTime == since.PhysicalTime && o.TimestampLogicalCounter > since.LogicalCounter)));
        if (collections != null)
        {
            var collectionSet = new HashSet<string>(collections);
            query = query.Where(o => collectionSet.Contains(o.Collection));
        }
        return await query
            .OrderBy(o => o.TimestampPhysicalTime)
            .ThenBy(o => o.TimestampLogicalCounter)
            .Select(e => e.ToDomain())
            .ToListAsync(cancellationToken);
    }

    public override async Task ImportAsync(IEnumerable<OplogEntry> items, CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            await _context.OplogEntries.InsertAsync(item.ToEntity(), cancellationToken);
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    public override async Task MergeAsync(IEnumerable<OplogEntry> items, CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            // Hash is now a regular indexed property, not the Key
            var existing = await _context.OplogEntries.AsQueryable().FirstOrDefaultAsync(o => o.Hash == item.Hash, cancellationToken);
            if (existing == null)
            {
                await _context.OplogEntries.InsertAsync(item.ToEntity(), cancellationToken);
            }
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    public override async Task PruneOplogAsync(HlcTimestamp cutoff, CancellationToken cancellationToken = default)
    {
        var toDelete = await _context.OplogEntries.AsQueryable()
            .Where(o => (o.TimestampPhysicalTime < cutoff.PhysicalTime) ||
                       (o.TimestampPhysicalTime == cutoff.PhysicalTime && o.TimestampLogicalCounter <= cutoff.LogicalCounter))
            .ToListAsync(cancellationToken);

        if (toDelete.Count == 0) return;

        // Checkpoint the latest-per-node entry being pruned into SnapshotMetadataStore first, so
        // InitializeVectorClock's Step 1 keeps a floor for a node after its last oplog row is gone.
        // Without it the VectorClock rehydrates empty once the oplog ages past retention, and peers
        // see the node as if it had never written anything. Matches EfCoreOplogStore.PruneOplogAsync.
        if (_snapshotMetadataStore != null)
        {
            var boundaryEntries = toDelete
                .GroupBy(o => o.TimestampNodeId)
                .Select(g => g.OrderByDescending(o => o.TimestampPhysicalTime)
                              .ThenByDescending(o => o.TimestampLogicalCounter)
                              .First());

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
                    }, cancellationToken);
                }
                else
                {
                    existingMeta.TimestampPhysicalTime = entry.TimestampPhysicalTime;
                    existingMeta.TimestampLogicalCounter = entry.TimestampLogicalCounter;
                    existingMeta.Hash = entry.Hash ?? "";
                    await _snapshotMetadataStore.UpdateSnapshotMetadataAsync(existingMeta, cancellationToken);
                }
            }
        }

        // Id (technical key), not Hash (business key) - see DropAsync above for the same distinction.
        await _context.OplogEntries.DeleteBulkAsync(toDelete.Select(o => o.Id), cancellationToken);
    }

    protected override void InitializeVectorClock()
    {
        if (_vectorClock.IsInitialized) return;

        // Step 1: Load from SnapshotMetadata FIRST (base state after prune)
        if (_snapshotMetadataStore != null)
        {
            try
            {
                var snapshots = _snapshotMetadataStore.GetAllSnapshotMetadataAsync().GetAwaiter().GetResult();
                foreach (var snapshot in snapshots)
                {
                    _vectorClock.UpdateNode(
                        snapshot.NodeId,
                        new HlcTimestamp(snapshot.TimestampPhysicalTime, snapshot.TimestampLogicalCounter, snapshot.NodeId),
                        snapshot.Hash ?? "");
                }
            }
            catch (Exception ex)
            {
                // Oplog data (Step 2) is used as fallback - a failure here should not block startup.
                _logger.LogWarning(ex, "Failed to load snapshot metadata during VectorClock initialization.");
            }
        }

        // Step 2: Load from Oplog (Latest State - Overrides Snapshot if newer)
        var latestPerNode = _context.OplogEntries.AsQueryable()
            .GroupBy(o => o.TimestampNodeId)
            .Select(g => new
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
                _vectorClock.UpdateNode(
                    node.NodeId,
                    new HlcTimestamp(node.MaxEntry.TimestampPhysicalTime, node.MaxEntry.TimestampLogicalCounter, node.MaxEntry.TimestampNodeId),
                    node.MaxEntry.Hash ?? "");
            }
        }

        _vectorClock.IsInitialized = true;
    }

    protected override async Task InsertOplogEntryAsync(OplogEntry entry, CancellationToken cancellationToken = default)
    {
        await _context.OplogEntries.InsertAsync(entry.ToEntity(), cancellationToken);
    }

    protected override async Task<string?> QueryLastHashForNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        var lastEntry = await _context.OplogEntries.AsQueryable()
            .Where(o => o.TimestampNodeId == nodeId)
            .OrderByDescending(o => o.TimestampPhysicalTime)
            .ThenByDescending(o => o.TimestampLogicalCounter)
            .FirstOrDefaultAsync(cancellationToken);
        return lastEntry?.Hash;
    }

    protected override async Task<(long Wall, int Logic)?> QueryLastHashTimestampFromOplogAsync(string hash, CancellationToken cancellationToken = default)
    {
        // Hash is now a regular indexed property, not the Key
        var entry = await _context.OplogEntries.AsQueryable().FirstOrDefaultAsync(o => o.Hash == hash, cancellationToken);
        if (entry == null) return null;
        return (entry.TimestampPhysicalTime, entry.TimestampLogicalCounter);
    }
}
