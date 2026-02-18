using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using EntglDb.Core.Network;
using EntglDb.Persistence.EntityFramework.Entities;
using EntglDb.Persistence.Sqlite;

namespace EntglDb.Persistence.EntityFramework;

public class EfCorePeerConfigurationStore<TDbContext> : PeerConfigurationStore where TDbContext : DbContext
{
    protected readonly TDbContext _context;
    protected readonly ILogger<EfCorePeerConfigurationStore<TDbContext>> _logger;

    public EfCorePeerConfigurationStore(TDbContext context, ILogger<EfCorePeerConfigurationStore<TDbContext>> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override async Task DropAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Dropping peer configuration store - all remote peer configurations will be permanently deleted!");
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _context.Set<RemotePeerEntity>().RemoveRange(_context.Set<RemotePeerEntity>());
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Peer configuration store dropped successfully.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError("Failed to drop peer configuration store.");
            throw;
        }
    }

    public override async Task<IEnumerable<RemotePeerConfiguration>> ExportAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _context.Set<RemotePeerEntity>().ToListAsync(cancellationToken);
        return entities.Select(e => new RemotePeerConfiguration
        {
            NodeId = e.NodeId,
            Address = e.Address,
            Type = (PeerType)e.Type,
            OAuth2Json = e.OAuth2Json,
            IsEnabled = e.IsEnabled,
            InterestingCollections = !string.IsNullOrEmpty(e.InterestsJson)
                ? JsonSerializer.Deserialize<List<string>>(e.InterestsJson) ?? new List<string>()
                : new List<string>()
        });
    }

    /// <inheritdoc />
    public override async Task<RemotePeerConfiguration?> GetRemotePeerAsync(string nodeId, CancellationToken cancellationToken)
    {
        var entity = await _context.Set<RemotePeerEntity>()
            .FirstOrDefaultAsync(p => p.NodeId == nodeId, cancellationToken);

        if (entity == null)
        {
            return null; // Peer not found
        }

        return new RemotePeerConfiguration
        {
            NodeId = entity.NodeId,
            Address = entity.Address,
            Type = (PeerType)entity.Type,
            OAuth2Json = entity.OAuth2Json,
            IsEnabled = entity.IsEnabled,
            InterestingCollections = !string.IsNullOrEmpty(entity.InterestsJson)
                ? JsonSerializer.Deserialize<List<string>>(entity.InterestsJson) ?? new List<string>()
                : new List<string>()
        };
    }

    /// <inheritdoc />
    public override async Task<IEnumerable<RemotePeerConfiguration>> GetRemotePeersAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _context.Set<RemotePeerEntity>().ToListAsync(cancellationToken);

        return entities.Select(e => new RemotePeerConfiguration
        {
            NodeId = e.NodeId,
            Address = e.Address,
            Type = (PeerType)e.Type,
            OAuth2Json = e.OAuth2Json,
            IsEnabled = e.IsEnabled,
            InterestingCollections = !string.IsNullOrEmpty(e.InterestsJson)
                ? JsonSerializer.Deserialize<List<string>>(e.InterestsJson) ?? new List<string>()
                : new List<string>()
        });
    }

    /// <inheritdoc />
    public override async Task RemoveRemotePeerAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Set<RemotePeerEntity>()
            .FirstOrDefaultAsync(p => p.NodeId == nodeId, cancellationToken);

        if (entity != null)
        {
            _context.Set<RemotePeerEntity>().Remove(entity);
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Removed remote peer configuration: {NodeId}", nodeId);
        }
        else
        {
            _logger.LogWarning("Attempted to remove non-existent remote peer: {NodeId}", nodeId);
        }
    }

    /// <inheritdoc />
    public override async Task SaveRemotePeerAsync(RemotePeerConfiguration peer, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Set<RemotePeerEntity>()
            .FirstOrDefaultAsync(p => p.NodeId == peer.NodeId, cancellationToken);

        if (entity == null)
        {
            entity = new RemotePeerEntity
            {
                NodeId = peer.NodeId
            };
            _context.Set<RemotePeerEntity>().Add(entity);
        }

        entity.Address = peer.Address;
        entity.Type = (int)peer.Type;
        entity.OAuth2Json = peer.OAuth2Json;
        entity.IsEnabled = peer.IsEnabled;
        entity.InterestsJson = peer.InterestingCollections != null && peer.InterestingCollections.Any()
            ? JsonSerializer.Serialize(peer.InterestingCollections)
            : null;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Saved remote peer configuration: {NodeId} ({Type})", peer.NodeId, peer.Type);
    }
}
