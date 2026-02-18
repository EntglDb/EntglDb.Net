using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using EntglDb.Core;
using EntglDb.Persistence.EntityFramework.Entities;
using EntglDb.Persistence.Sqlite;

namespace EntglDb.Persistence.EntityFramework;

/// <summary>
/// Provides a snapshot metadata store implementation that uses Entity Framework Core for persistent storage of snapshot
/// metadata.
/// </summary>
/// <remarks>This class enables integration of snapshot metadata management with any Entity Framework Core-backed
/// data store by supplying a compatible DbContext. It is suitable for scenarios where snapshot metadata needs to be
/// stored, queried, and managed within a relational database using EF Core. Thread safety depends on the underlying
/// DbContext implementation; typically, DbContext instances are not thread-safe and should not be shared across
/// threads.</remarks>
/// <typeparam name="TDbContext">The type of the Entity Framework Core database context to be used for data access operations. Must inherit from
/// DbContext.</typeparam>
public class EfCoreSnapshotMetadaStore<TDbContext> : SnapshotMetadataStore where TDbContext : DbContext
{
    private readonly TDbContext _context;
    private readonly ILogger<EfCoreSnapshotMetadaStore<TDbContext>> _logger;

    /// <summary>
    /// Initializes a new instance of the EfCoreSnapshotMetadaStore class using the specified database context and
    /// optional logger.
    /// </summary>
    /// <param name="context">The Entity Framework Core database context to be used for data access operations. Cannot be null.</param>
    /// <param name="logger">An optional logger used to record diagnostic information and errors. If null, a no-op logger is used.</param>
    /// <exception cref="ArgumentNullException">Thrown if the context parameter is null.</exception>
    public EfCoreSnapshotMetadaStore(TDbContext context, ILogger<EfCoreSnapshotMetadaStore<TDbContext>>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? NullLogger<EfCoreSnapshotMetadaStore<TDbContext>>.Instance;
    }

    /// <inheritdoc />
    public override async Task DropAsync(CancellationToken cancellationToken = default)
    {
        await _context.Database.ExecuteSqlRawAsync("DELETE FROM SnapshotMetadata", cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<IEnumerable<SnapshotMetadata>> ExportAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _context.Set<SnapshotMetadataEntity>().ToListAsync(cancellationToken);
        return entities.Select(e => new SnapshotMetadata
        {
            NodeId = e.NodeId,
            TimestampPhysicalTime = e.TimestampPhysicalTime,
            TimestampLogicalCounter = e.TimestampLogicalCounter,
            Hash = e.Hash
        });
    }

    /// <inheritdoc />
    public override async Task ImportAsync(IEnumerable<SnapshotMetadata> items, CancellationToken cancellationToken = default)
    {
        foreach (var metadata in items)
        {
            var entity = new SnapshotMetadataEntity
            {
                NodeId = metadata.NodeId,
                TimestampPhysicalTime = metadata.TimestampPhysicalTime,
                TimestampLogicalCounter = metadata.TimestampLogicalCounter,
                Hash = metadata.Hash
            };
            _context.Set<SnapshotMetadataEntity>().Add(entity);
        }
        if (_context.Database.CurrentTransaction == null)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public override async Task InsertSnapshotMetadataAsync(SnapshotMetadata metadata, CancellationToken cancellationToken = default)
    {
        var entity = new SnapshotMetadataEntity
        {
            NodeId = metadata.NodeId,
            TimestampPhysicalTime = metadata.TimestampPhysicalTime,
            TimestampLogicalCounter = metadata.TimestampLogicalCounter,
            Hash = metadata.Hash
        };
        _context.Set<SnapshotMetadataEntity>().Add(entity);
        if (_context.Database.CurrentTransaction == null)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public override async Task MergeAsync(IEnumerable<SnapshotMetadata> items, CancellationToken cancellationToken = default)
    {
            foreach (var metadata in items)
            {
                var existing = await _context.Set<SnapshotMetadataEntity>()
                    .FirstOrDefaultAsync(s => s.NodeId == metadata.NodeId, cancellationToken);
    
                if (existing == null)
                {
                    _context.Set<SnapshotMetadataEntity>().Add(new SnapshotMetadataEntity
                    {
                        NodeId = metadata.NodeId,
                        TimestampPhysicalTime = metadata.TimestampPhysicalTime,
                        TimestampLogicalCounter = metadata.TimestampLogicalCounter,
                        Hash = metadata.Hash
                    });
                }
                else
                {
                    existing.TimestampPhysicalTime = metadata.TimestampPhysicalTime;
                    existing.TimestampLogicalCounter = metadata.TimestampLogicalCounter;
                    existing.Hash = metadata.Hash;
                    _context.Set<SnapshotMetadataEntity>().Update(existing);
                }
            }

        if (_context.Database.CurrentTransaction == null)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public override async Task<string?> GetSnapshotHashAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        var snapshot = await _context.Set<SnapshotMetadataEntity>()
            .Where(s => s.NodeId == nodeId)
            .FirstOrDefaultAsync(cancellationToken);

        return snapshot?.Hash;
    }

    public override async Task<SnapshotMetadata?> GetSnapshotMetadataAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        var snapshot = await _context.Set<SnapshotMetadataEntity>()
            .Where(s => s.NodeId == nodeId)
            .FirstOrDefaultAsync(cancellationToken);
        if (snapshot == null)
        {
            return null;
        }
        return new SnapshotMetadata
        {
            NodeId = snapshot.NodeId,
            TimestampPhysicalTime = snapshot.TimestampPhysicalTime,
            TimestampLogicalCounter = snapshot.TimestampLogicalCounter,
            Hash = snapshot.Hash
        };
    }

    public override async Task<IEnumerable<SnapshotMetadata>> GetAllSnapshotMetadataAsync(CancellationToken cancellationToken = default)
    {
        var snapshots = await _context.Set<SnapshotMetadataEntity>()
            .ToListAsync(cancellationToken);

        return snapshots.Select(s => new SnapshotMetadata
        {
            NodeId = s.NodeId,
            TimestampPhysicalTime = s.TimestampPhysicalTime,
            TimestampLogicalCounter = s.TimestampLogicalCounter,
            Hash = s.Hash
        });
    }

    public override async Task UpdateSnapshotMetadataAsync(SnapshotMetadata existingMeta, CancellationToken cancellationToken)
    {
        var entity = await _context.Set<SnapshotMetadataEntity>()
            .Where(s => s.NodeId == existingMeta.NodeId)
            .FirstOrDefaultAsync(cancellationToken);
        if (entity == null)
        {
            await InsertSnapshotMetadataAsync(existingMeta, cancellationToken);
            return;
        }
        entity.TimestampPhysicalTime = existingMeta.TimestampPhysicalTime;
        entity.TimestampLogicalCounter = existingMeta.TimestampLogicalCounter;
        entity.Hash = existingMeta.Hash;
        _context.Set<SnapshotMetadataEntity>().Update(entity);
        if(_context.Database.CurrentTransaction == null)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
