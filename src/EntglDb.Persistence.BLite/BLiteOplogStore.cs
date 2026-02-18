using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Persistence.BLite.Entities;
using EntglDb.Persistence.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Persistence.BLite;

public class BLiteOplogStore<TDbContext> : OplogStore where TDbContext : EntglDocumentDbContext
{
    protected readonly TDbContext _context;
    protected readonly ILogger<BLiteOplogStore<TDbContext>> _logger;

    public BLiteOplogStore(
        TDbContext dbContext, 
        IDocumentStore documentStore, 
        IConflictResolver conflictResolver,
        IVectorClockService vectorClockService,
        ISnapshotMetadataStore? snapshotMetadataStore = null,
        ILogger<BLiteOplogStore<TDbContext>>? logger = null) : base(documentStore, conflictResolver, vectorClockService, snapshotMetadataStore)
    {
        _context = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? NullLogger<BLiteOplogStore<TDbContext>>.Instance;
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
        await _context.OplogEntries.DeleteBulkAsync(_context.OplogEntries.FindAll().Select(e => e.Id));
        await _context.SaveChangesAsync(cancellationToken);
        _vectorClock.Invalidate();
    }

    public override async Task<IEnumerable<OplogEntry>> ExportAsync(CancellationToken cancellationToken = default)
    {
        return _context.OplogEntries.FindAll().ToDomain();
    }

    public override async Task<IEnumerable<OplogEntry>> GetChainRangeAsync(string startHash, string endHash, CancellationToken cancellationToken = default)
    {
        var startRow = _context.OplogEntries.FindById(startHash);
        var endRow = _context.OplogEntries.FindById(endHash);

        if (startRow == null || endRow == null) return [];

        var nodeId = startRow.TimestampNodeId;

        // 2. Fetch range (Start < Entry <= End)
        var entities = _context.OplogEntries
            .Find(o => o.TimestampNodeId == nodeId &&
                       ((o.TimestampPhysicalTime > startRow.TimestampPhysicalTime) ||
                        (o.TimestampPhysicalTime == startRow.TimestampPhysicalTime && o.TimestampLogicalCounter > startRow.TimestampLogicalCounter)) &&
                       ((o.TimestampPhysicalTime < endRow.TimestampPhysicalTime) ||
                        (o.TimestampPhysicalTime == endRow.TimestampPhysicalTime && o.TimestampLogicalCounter <= endRow.TimestampLogicalCounter)))
            .OrderBy(o => o.TimestampPhysicalTime)
            .ThenBy(o => o.TimestampLogicalCounter)
            .ToList();

        return entities.ToDomain();
    }

    public override async Task<OplogEntry?> GetEntryByHashAsync(string hash, CancellationToken cancellationToken = default)
    {
        // Hash is now a regular indexed property, not the Key
        return _context.OplogEntries.Find(o => o.Hash == hash).FirstOrDefault()?.ToDomain();
    }

    public override async Task<IEnumerable<OplogEntry>> GetOplogAfterAsync(HlcTimestamp timestamp, IEnumerable<string>? collections = null, CancellationToken cancellationToken = default)
    {
        var query = _context.OplogEntries
            .Find(o => (o.TimestampPhysicalTime > timestamp.PhysicalTime) ||
                       (o.TimestampPhysicalTime == timestamp.PhysicalTime && o.TimestampLogicalCounter > timestamp.LogicalCounter));
        if (collections != null)
        {
            var collectionSet = new HashSet<string>(collections);
            query = query.Where(o => collectionSet.Contains(o.Collection));
        }
        return query
            .OrderBy(o => o.TimestampPhysicalTime)
            .ThenBy(o => o.TimestampLogicalCounter)
            .ToDomain()
            .ToList();
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
        return query
            .OrderBy(o => o.TimestampPhysicalTime)
            .ThenBy(o => o.TimestampLogicalCounter)
            .ToDomain()
            .ToList();
    }

    public override async Task ImportAsync(IEnumerable<OplogEntry> items, CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            await _context.OplogEntries.InsertAsync(item.ToEntity());
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    public override async Task MergeAsync(IEnumerable<OplogEntry> items, CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            // Hash is now a regular indexed property, not the Key
            var existing = _context.OplogEntries.Find(o => o.Hash == item.Hash).FirstOrDefault();
            if (existing == null)
            {
                await _context.OplogEntries.InsertAsync(item.ToEntity());
            }
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    public override async Task PruneOplogAsync(HlcTimestamp cutoff, CancellationToken cancellationToken = default)
    {
        var toDelete = _context.OplogEntries.AsQueryable()
            .Where(o => (o.TimestampPhysicalTime < cutoff.PhysicalTime) ||
                       (o.TimestampPhysicalTime == cutoff.PhysicalTime && o.TimestampLogicalCounter <= cutoff.LogicalCounter))
            .Select(o => o.Hash)
            .ToList();
        await _context.OplogEntries.DeleteBulkAsync(toDelete);
    }

    protected override void InitializeVectorClock()
    {
        if (_vectorClock.IsInitialized) return;

        // Early check: if context or OplogEntries is null, skip initialization
        if (_context?.OplogEntries == null)
        {
            _vectorClock.IsInitialized = true;
            return;
        }

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
            catch
            {
                // Ignore errors during initialization - oplog data will be used as fallback
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
        await _context.OplogEntries.InsertAsync(entry.ToEntity());
    }

    protected override async Task<string?> QueryLastHashForNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        var lastEntry = _context.OplogEntries.AsQueryable()
            .Where(o => o.TimestampNodeId == nodeId)
            .OrderByDescending(o => o.TimestampPhysicalTime)
            .ThenByDescending(o => o.TimestampLogicalCounter)
            .FirstOrDefault();
        return lastEntry?.Hash;
    }

    protected override async Task<(long Wall, int Logic)?> QueryLastHashTimestampFromOplogAsync(string hash, CancellationToken cancellationToken = default)
    {
        // Hash is now a regular indexed property, not the Key
        var entry = _context.OplogEntries.Find(o => o.Hash == hash).FirstOrDefault();
        if (entry == null) return null;
        return (entry.TimestampPhysicalTime, entry.TimestampLogicalCounter);
    }
}
