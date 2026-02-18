using EntglDb.Core.Network;
using EntglDb.Persistence.BLite.Entities;
using EntglDb.Persistence.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Persistence.BLite;

/// <summary>
/// Provides a peer configuration store implementation that uses a specified EntglDocumentDbContext for persistence
/// operations.
/// </summary>
/// <remarks>This class enables storage, retrieval, and management of remote peer configurations using the provided
/// database context. It is typically used in scenarios where peer configurations need to be persisted in a document
/// database.</remarks>
/// <typeparam name="TDbContext">The type of the document database context used for accessing and managing peer configurations. Must inherit from
/// EntglDocumentDbContext.</typeparam>
public class BLitePeerConfigurationStore<TDbContext> : PeerConfigurationStore where TDbContext : EntglDocumentDbContext
{
    /// <summary>
    /// Represents the database context used for data access operations within the derived class.
    /// </summary>
    protected readonly TDbContext _context;

    /// <summary>
    /// Provides logging capabilities for the BLitePeerConfigurationStore operations.
    /// </summary>
    protected readonly ILogger<BLitePeerConfigurationStore<TDbContext>> _logger;

    /// <summary>
    /// Initializes a new instance of the BLitePeerConfigurationStore class using the specified database context and
    /// optional logger.
    /// </summary>
    /// <param name="context">The database context used to access and manage peer configuration data. Cannot be null.</param>
    /// <param name="logger">An optional logger for logging diagnostic messages. If null, a no-op logger is used.</param>
    /// <exception cref="ArgumentNullException">Thrown if the context parameter is null.</exception>
    public BLitePeerConfigurationStore(TDbContext context, ILogger<BLitePeerConfigurationStore<TDbContext>>? logger = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? NullLogger<BLitePeerConfigurationStore<TDbContext>>.Instance;
    }

    /// <inheritdoc />
    public override async Task DropAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Dropping peer configuration store - all remote peer configurations will be permanently deleted!");
        // Use Id (technical key) for deletion, not NodeId (business key)
        var allIds = await Task.Run(() => _context.RemotePeerConfigurations.FindAll().Select(p => p.Id).ToList(), cancellationToken);
        await _context.RemotePeerConfigurations.DeleteBulkAsync(allIds);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Peer configuration store dropped successfully.");
    }

    /// <inheritdoc />
    public override async Task<IEnumerable<RemotePeerConfiguration>> ExportAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => _context.RemotePeerConfigurations.FindAll().ToDomain().ToList(), cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<RemotePeerConfiguration?> GetRemotePeerAsync(string nodeId, CancellationToken cancellationToken)
    {
        // NodeId is now a regular indexed property, not the Key
        return await Task.Run(() => _context.RemotePeerConfigurations.Find(p => p.NodeId == nodeId).FirstOrDefault()?.ToDomain(), cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<IEnumerable<RemotePeerConfiguration>> GetRemotePeersAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => _context.RemotePeerConfigurations.FindAll().ToDomain().ToList(), cancellationToken);
    }

    /// <inheritdoc />
    public override async Task RemoveRemotePeerAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        // NodeId is now a regular indexed property, not the Key
        var peer = await Task.Run(() => _context.RemotePeerConfigurations.Find(p => p.NodeId == nodeId).FirstOrDefault(), cancellationToken);
        if (peer != null)
        {
            await _context.RemotePeerConfigurations.DeleteAsync(peer.Id);
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
        var existing = await Task.Run(() => _context.RemotePeerConfigurations.Find(p => p.NodeId == peer.NodeId).FirstOrDefault(), cancellationToken);

        if (existing == null)
        {
            await _context.RemotePeerConfigurations.InsertAsync(peer.ToEntity());
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
            await _context.RemotePeerConfigurations.UpdateAsync(existing);
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Saved remote peer configuration: {NodeId} ({Type})", peer.NodeId, peer.Type);
    }
}
