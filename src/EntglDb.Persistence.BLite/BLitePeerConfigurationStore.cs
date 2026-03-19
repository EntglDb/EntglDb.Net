using BLite.Core.Query;
using EntglDb.Core.Network;
using EntglDb.Persistence.BLite.Entities;
using EntglDb.Persistence.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Persistence.BLite;

/// <summary>
/// Provides a peer configuration store implementation using EntglDbMetaContext for persistence.
/// </summary>
public class BLitePeerConfigurationStore : PeerConfigurationStore
{
    protected readonly EntglDbMetaContext _context;
    protected readonly ILogger<BLitePeerConfigurationStore> _logger;

    public BLitePeerConfigurationStore(EntglDbMetaContext context, ILogger<BLitePeerConfigurationStore>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? NullLogger<BLitePeerConfigurationStore>.Instance;
    }

    /// <inheritdoc />
    public override async Task DropAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Dropping peer configuration store - all remote peer configurations will be permanently deleted!");
        // Use Id (technical key) for deletion, not NodeId (business key)
        var allIds = await _context.RemotePeerConfigurations.AsQueryable().Select(p => p.Id).ToListAsync(cancellationToken);
        await _context.RemotePeerConfigurations.DeleteBulkAsync(allIds, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Peer configuration store dropped successfully.");
    }

    /// <inheritdoc />
    public override async Task<IEnumerable<RemotePeerConfiguration>> ExportAsync(CancellationToken cancellationToken = default)
    {
        return await _context.RemotePeerConfigurations.AsQueryable().Select(e => e.ToDomain()).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<RemotePeerConfiguration?> GetRemotePeerAsync(string nodeId, CancellationToken cancellationToken)
    {
        // NodeId is now a regular indexed property, not the Key
        return await _context.RemotePeerConfigurations.AsQueryable()
            .Where(p => p.NodeId == nodeId)
            .Select(p => p.ToDomain())
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<IEnumerable<RemotePeerConfiguration>> GetRemotePeersAsync(CancellationToken cancellationToken = default)
    {
        return await _context.RemotePeerConfigurations.AsQueryable().Select(e => e.ToDomain()).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override async Task RemoveRemotePeerAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        // NodeId is now a regular indexed property, not the Key
        var peer = await _context.RemotePeerConfigurations.AsQueryable().FirstOrDefaultAsync(p => p.NodeId == nodeId, cancellationToken);
        if (peer != null)
        {
            await _context.RemotePeerConfigurations.DeleteAsync(peer.Id, cancellationToken);
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
        // NodeId is now a regular indexed property, not the Key
        var existing = await _context.RemotePeerConfigurations.AsQueryable().FirstOrDefaultAsync(p => p.NodeId == peer.NodeId, cancellationToken);

        if (existing == null)
        {
            await _context.RemotePeerConfigurations.InsertAsync(peer.ToEntity(), cancellationToken);
        }
        else
        {
            existing.NodeId = peer.NodeId;
            existing.Address = peer.Address;
            existing.Type = (int)peer.Type;
            existing.OAuth2Json = peer.OAuth2Json ?? "";
            existing.IsEnabled = peer.IsEnabled;
            existing.InterestsJson = peer.InterestingCollections.Count > 0
                ? System.Text.Json.JsonSerializer.Serialize(peer.InterestingCollections)
                : "";
            await _context.RemotePeerConfigurations.UpdateAsync(existing, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Saved remote peer configuration: {NodeId} ({Type})", peer.NodeId, peer.Type);
    }
}
