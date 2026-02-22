using BLite.Core.Query;
using EntglDb.Core;
using EntglDb.Persistence.BLite.Entities;
using EntglDb.Persistence.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Persistence.BLite;

/// <summary>
/// Provides a snapshot metadata store implementation that uses a specified EntglDocumentDbContext for persistence
/// operations.
/// </summary>
/// <remarks>This class enables storage, retrieval, and management of snapshot metadata using the provided
/// database context. It is typically used in scenarios where snapshot metadata needs to be persisted in a document
/// database. The class supports bulk operations and incremental updates, and can be extended for custom database
/// contexts. Thread safety depends on the underlying context implementation.</remarks>
/// <typeparam name="TDbContext">The type of the document database context used for accessing and managing snapshot metadata. Must inherit from
/// EntglDocumentDbContext.</typeparam>
public class BLiteSnapshotMetadataStore<TDbContext> : SnapshotMetadataStore where TDbContext : EntglDocumentDbContext
{
    /// <summary>
    /// Represents the database context used for data access operations within the derived class.
    /// </summary>
    /// <remarks>Intended for use by derived classes to interact with the underlying database. The context
    /// should be properly disposed of according to the application's lifetime management strategy.</remarks>
    protected readonly TDbContext _context;

    /// <summary>
    /// Provides logging capabilities for the BLiteSnapshotMetadataStore operations.
    /// </summary>
    /// <remarks>Intended for use by derived classes to record diagnostic and operational information. The
    /// logger instance is specific to the BLiteSnapshotMetadataStore<TDbContext> type.</remarks>
    protected readonly ILogger<BLiteSnapshotMetadataStore<TDbContext>> _logger;

    /// <summary>
    /// Initializes a new instance of the BLiteSnapshotMetadataStore class using the specified database context and
    /// optional logger.
    /// </summary>
    /// <param name="context">The database context to be used for accessing snapshot metadata. Cannot be null.</param>
    /// <param name="logger">An optional logger for logging diagnostic messages. If null, a no-op logger is used.</param>
    /// <exception cref="ArgumentNullException">Thrown if the context parameter is null.</exception>
    public BLiteSnapshotMetadataStore(TDbContext context, ILogger<BLiteSnapshotMetadataStore<TDbContext>>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? NullLogger<BLiteSnapshotMetadataStore<TDbContext>>.Instance;
    }

    /// <inheritdoc />
    public override async Task DropAsync(CancellationToken cancellationToken = default)
    {
        // Use Id (technical key) for deletion, not NodeId (business key)
        var allIds = await _context.SnapshotMetadatas.AsQueryable().Select(s => s.Id).ToListAsync(cancellationToken);
        await _context.SnapshotMetadatas.DeleteBulkAsync(allIds);
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
            await _context.SnapshotMetadatas.InsertAsync(metadata.ToEntity());
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task InsertSnapshotMetadataAsync(SnapshotMetadata metadata, CancellationToken cancellationToken = default)
    {
        await _context.SnapshotMetadatas.InsertAsync(metadata.ToEntity());
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
                await _context.SnapshotMetadatas.InsertAsync(metadata.ToEntity());
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
                    await _context.SnapshotMetadatas.UpdateAsync(existing);
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
            await _context.SnapshotMetadatas.UpdateAsync(existing);
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