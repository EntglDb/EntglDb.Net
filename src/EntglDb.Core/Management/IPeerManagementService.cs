using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core.Network;
using EntglDb.Core.Network;

namespace EntglDb.Core.Management;

/// <summary>
/// Service for managing remote peer configurations.
/// Provides CRUD operations for adding, removing, enabling/disabling remote cloud nodes.
/// </summary>
public interface IPeerManagementService
{
    /// <summary>
    /// Adds a cloud remote peer with OAuth2 authentication.
    /// </summary>
    /// <param name="nodeId">Unique identifier for the remote peer.</param>
    /// <param name="address">Network address (hostname:port) of the remote peer.</param>
    /// <param name="oauth2Config">OAuth2 configuration for authentication.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddCloudPeerAsync(string nodeId, string address, OAuth2Configuration oauth2Config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a static remote peer with simple authentication.
    /// </summary>
    /// <param name="nodeId">Unique identifier for the remote peer.</param>
    /// <param name="address">Network address (hostname:port) of the remote peer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddStaticPeerAsync(string nodeId, string address, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a remote peer configuration.
    /// </summary>
    /// <param name="nodeId">Unique identifier of the peer to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveRemotePeerAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all configured remote peers.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of remote peer configurations.</returns>
    Task<IEnumerable<RemotePeerConfiguration>> GetAllRemotePeersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables synchronization with a remote peer.
    /// </summary>
    /// <param name="nodeId">Unique identifier of the peer to enable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EnablePeerAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables synchronization with a remote peer (keeps configuration).
    /// </summary>
    /// <param name="nodeId">Unique identifier of the peer to disable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DisablePeerAsync(string nodeId, CancellationToken cancellationToken = default);
}
