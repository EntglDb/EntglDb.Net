using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Persistence.BLite.Entities;
using EntglDb.Persistence.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Persistence.BLite;

/// <summary>
/// BLite implementation of document metadata storage for sync tracking.
/// </summary>
/// <typeparam name="TDbContext">The type of EntglDocumentDbContext.</typeparam>
public class BLiteDocumentMetadataStore<TDbContext> : DocumentMetadataStore where TDbContext : EntglDocumentDbContext
{
    private readonly TDbContext _context;
    private readonly ILogger<BLiteDocumentMetadataStore<TDbContext>> _logger;

    public BLiteDocumentMetadataStore(TDbContext context, ILogger<BLiteDocumentMetadataStore<TDbContext>>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? NullLogger<BLiteDocumentMetadataStore<TDbContext>>.Instance;
    }

    /// <inheritdoc />
    public override async Task<DocumentMetadata?> GetMetadataAsync(string collection, string key, CancellationToken cancellationToken = default)
    {
        var entity = _context.DocumentMetadatas
            .Find(m => m.Collection == collection && m.Key == key)
            .FirstOrDefault();

        return entity != null ? ToDomain(entity) : null;
    }

    /// <inheritdoc />
    public override async Task<IEnumerable<DocumentMetadata>> GetMetadataByCollectionAsync(string collection, CancellationToken cancellationToken = default)
    {
        return _context.DocumentMetadatas
            .Find(m => m.Collection == collection)
            .Select(ToDomain)
            .ToList();
    }

    /// <inheritdoc />
    public override async Task UpsertMetadataAsync(DocumentMetadata metadata, CancellationToken cancellationToken = default)
    {
        var existing = _context.DocumentMetadatas
            .Find(m => m.Collection == metadata.Collection && m.Key == metadata.Key)
            .FirstOrDefault();

        if (existing == null)
        {
            await _context.DocumentMetadatas.InsertAsync(ToEntity(metadata));
        }
        else
        {
            existing.HlcPhysicalTime = metadata.UpdatedAt.PhysicalTime;
            existing.HlcLogicalCounter = metadata.UpdatedAt.LogicalCounter;
            existing.HlcNodeId = metadata.UpdatedAt.NodeId;
            existing.IsDeleted = metadata.IsDeleted;
            await _context.DocumentMetadatas.UpdateAsync(existing);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task UpsertMetadataBatchAsync(IEnumerable<DocumentMetadata> metadatas, CancellationToken cancellationToken = default)
    {
        foreach (var metadata in metadatas)
        {
            var existing = _context.DocumentMetadatas
                .Find(m => m.Collection == metadata.Collection && m.Key == metadata.Key)
                .FirstOrDefault();

            if (existing == null)
            {
                await _context.DocumentMetadatas.InsertAsync(ToEntity(metadata));
            }
            else
            {
                existing.HlcPhysicalTime = metadata.UpdatedAt.PhysicalTime;
                existing.HlcLogicalCounter = metadata.UpdatedAt.LogicalCounter;
                existing.HlcNodeId = metadata.UpdatedAt.NodeId;
                existing.IsDeleted = metadata.IsDeleted;
                await _context.DocumentMetadatas.UpdateAsync(existing);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task MarkDeletedAsync(string collection, string key, HlcTimestamp timestamp, CancellationToken cancellationToken = default)
    {
        var existing = _context.DocumentMetadatas
            .Find(m => m.Collection == collection && m.Key == key)
            .FirstOrDefault();

        if (existing == null)
        {
            await _context.DocumentMetadatas.InsertAsync(new DocumentMetadataEntity
            {
                Id = Guid.NewGuid().ToString(),
                Collection = collection,
                Key = key,
                HlcPhysicalTime = timestamp.PhysicalTime,
                HlcLogicalCounter = timestamp.LogicalCounter,
                HlcNodeId = timestamp.NodeId,
                IsDeleted = true
            });
        }
        else
        {
            existing.HlcPhysicalTime = timestamp.PhysicalTime;
            existing.HlcLogicalCounter = timestamp.LogicalCounter;
            existing.HlcNodeId = timestamp.NodeId;
            existing.IsDeleted = true;
            await _context.DocumentMetadatas.UpdateAsync(existing);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<IEnumerable<DocumentMetadata>> GetMetadataAfterAsync(HlcTimestamp since, IEnumerable<string>? collections = null, CancellationToken cancellationToken = default)
    {
        var query = _context.DocumentMetadatas.AsQueryable()
            .Where(m => (m.HlcPhysicalTime > since.PhysicalTime) ||
                       (m.HlcPhysicalTime == since.PhysicalTime && m.HlcLogicalCounter > since.LogicalCounter));

        if (collections != null)
        {
            var collectionSet = new HashSet<string>(collections);
            query = query.Where(m => collectionSet.Contains(m.Collection));
        }

        return query
            .OrderBy(m => m.HlcPhysicalTime)
            .ThenBy(m => m.HlcLogicalCounter)
            .Select(ToDomain)
            .ToList();
    }

    /// <inheritdoc />
    public override async Task DropAsync(CancellationToken cancellationToken = default)
    {
        var allIds = _context.DocumentMetadatas.FindAll().Select(m => m.Id).ToList();
        await _context.DocumentMetadatas.DeleteBulkAsync(allIds);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<IEnumerable<DocumentMetadata>> ExportAsync(CancellationToken cancellationToken = default)
    {
        return _context.DocumentMetadatas.FindAll().Select(ToDomain).ToList();
    }

    /// <inheritdoc />
    public override async Task ImportAsync(IEnumerable<DocumentMetadata> items, CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            await _context.DocumentMetadatas.InsertAsync(ToEntity(item));
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task MergeAsync(IEnumerable<DocumentMetadata> items, CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            var existing = _context.DocumentMetadatas
                .Find(m => m.Collection == item.Collection && m.Key == item.Key)
                .FirstOrDefault();

            if (existing == null)
            {
                await _context.DocumentMetadatas.InsertAsync(ToEntity(item));
            }
            else
            {
                // Update only if incoming is newer
                var existingTs = new HlcTimestamp(existing.HlcPhysicalTime, existing.HlcLogicalCounter, existing.HlcNodeId);
                if (item.UpdatedAt.CompareTo(existingTs) > 0)
                {
                    existing.HlcPhysicalTime = item.UpdatedAt.PhysicalTime;
                    existing.HlcLogicalCounter = item.UpdatedAt.LogicalCounter;
                    existing.HlcNodeId = item.UpdatedAt.NodeId;
                    existing.IsDeleted = item.IsDeleted;
                    await _context.DocumentMetadatas.UpdateAsync(existing);
                }
            }
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    #region Mappers

    private static DocumentMetadata ToDomain(DocumentMetadataEntity entity)
    {
        return new DocumentMetadata(
            entity.Collection,
            entity.Key,
            new HlcTimestamp(entity.HlcPhysicalTime, entity.HlcLogicalCounter, entity.HlcNodeId),
            entity.IsDeleted
        );
    }

    private static DocumentMetadataEntity ToEntity(DocumentMetadata metadata)
    {
        return new DocumentMetadataEntity
        {
            Id = Guid.NewGuid().ToString(),
            Collection = metadata.Collection,
            Key = metadata.Key,
            HlcPhysicalTime = metadata.UpdatedAt.PhysicalTime,
            HlcLogicalCounter = metadata.UpdatedAt.LogicalCounter,
            HlcNodeId = metadata.UpdatedAt.NodeId,
            IsDeleted = metadata.IsDeleted
        };
    }

    #endregion
}
