using BLite.Core.Query;
using EntglDb.Core;
using EntglDb.Persistence.BLite.Entities;
using EntglDb.Persistence.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Persistence.BLite;

/// <summary>
/// Provides a snapshot metadata store implementation using EntglDbMetaContext for persistence.
/// </summary>
public class BLiteSnapshotMetadataStore : SnapshotMetadataStore
{
    protected readonly EntglDbMetaContext _context;
    protected readonly ILogger<BLiteSnapshotMetadataStore> _logger;

    public BLiteSnapshotMetadataStore(EntglDbMetaContext context, ILogger<BLiteSnapshotMetadataStore>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? NullLogger<BLiteSnapshotMetadataStore>.Instance;
    }

    /// <inheritdoc />
    public override async Task DropAsync(CancellationToken cancellationToken = default)
    {
        // Use Id (technical key) for deletion, not NodeId (business key)
        var allIds = await _context.SnapshotMetadatas.AsQueryable().Select(s => s.Id).ToListAsync(cancellationToken);
        await _context.SnapshotMetadatas.DeleteBulkAsync(allIds, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<IEnumerable<SnapshotMetadata>> ExportAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SnapshotMetadatas.AsQueryable().Select(e => e.ToDomain()).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<string?> GetSnapshotHashAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        // NodeId is now a regular indexed property, not the Key
        return await _context.SnapshotMetadatas.AsQueryable()
            .Where(s => s.NodeId == nodeId)
            .Select(s => s.Hash)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task ImportAsync(IEnumerable<SnapshotMetadata> items, CancellationToken cancellationToken = default)
    {
        foreach (var metadata in items)
        {
            await _context.SnapshotMetadatas.InsertAsync(metadata.ToEntity(), cancellationToken);
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task InsertSnapshotMetadataAsync(SnapshotMetadata metadata, CancellationToken cancellationToken = default)
    {
        await _context.SnapshotMetadatas.InsertAsync(metadata.ToEntity(), cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task MergeAsync(IEnumerable<SnapshotMetadata> items, CancellationToken cancellationToken = default)
    {
        foreach (var metadata in items)
        {
            // NodeId is now a regular indexed property, not the Key
            var existing = await _context.SnapshotMetadatas.AsQueryable().FirstOrDefaultAsync(s => s.NodeId == metadata.NodeId, cancellationToken);

            if (existing == null)
            {
                await _context.SnapshotMetadatas.InsertAsync(metadata.ToEntity(), cancellationToken);
            }
            else
            {
                // Update only if incoming is newer
                if (metadata.TimestampPhysicalTime > existing.TimestampPhysicalTime ||
                    (metadata.TimestampPhysicalTime == existing.TimestampPhysicalTime &&
                     metadata.TimestampLogicalCounter > existing.TimestampLogicalCounter))
                {
                    existing.NodeId = metadata.NodeId;
                    existing.TimestampPhysicalTime = metadata.TimestampPhysicalTime;
                    existing.TimestampLogicalCounter = metadata.TimestampLogicalCounter;
                    existing.Hash = metadata.Hash;
                    await _context.SnapshotMetadatas.UpdateAsync(existing, cancellationToken);
                }
            }
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task UpdateSnapshotMetadataAsync(SnapshotMetadata existingMeta, CancellationToken cancellationToken)
    {
        // NodeId is now a regular indexed property, not the Key - find existing by NodeId
        var existing = await _context.SnapshotMetadatas.AsQueryable().FirstOrDefaultAsync(s => s.NodeId == existingMeta.NodeId, cancellationToken);
        if (existing != null)
        {
            existing.NodeId = existingMeta.NodeId;
            existing.TimestampPhysicalTime = existingMeta.TimestampPhysicalTime;
            existing.TimestampLogicalCounter = existingMeta.TimestampLogicalCounter;
            existing.Hash = existingMeta.Hash;
            await _context.SnapshotMetadatas.UpdateAsync(existing, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public override async Task<SnapshotMetadata?> GetSnapshotMetadataAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        // NodeId is now a regular indexed property, not the Key
        return await _context.SnapshotMetadatas.AsQueryable()
            .Where(s => s.NodeId == nodeId)
            .Select(s => s.ToDomain())
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<IEnumerable<SnapshotMetadata>> GetAllSnapshotMetadataAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SnapshotMetadatas.AsQueryable().Select(e => e.ToDomain()).ToListAsync(cancellationToken);
    }
}