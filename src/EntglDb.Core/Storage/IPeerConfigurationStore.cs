using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core.Network;

namespace EntglDb.Core.Storage;

/// <summary>
/// Handles storage and retrieval of remote peer configurations.
/// </summary>
public interface IPeerConfigurationStore : ISnapshotable<RemotePeerConfiguration>
{
    /// <summary>
    /// Saves or updates a remote peer configuration in the persistent store.
    /// </summary>
    /// <param name="peer">The remote peer configuration to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveRemotePeerAsync(RemotePeerConfiguration peer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all remote peer configurations from the persistent store.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of remote peer configurations.</returns>
    Task<IEnumerable<RemotePeerConfiguration>> GetRemotePeersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves the configuration for a remote peer identified by the specified node ID.
    /// </summary>
    /// <param name="nodeId">The unique identifier of the remote peer whose configuration is to be retrieved.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task containing the RemotePeerConfiguration if found; otherwise, null.</returns>
    Task<RemotePeerConfiguration?> GetRemotePeerAsync(string nodeId, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a remote peer configuration from the persistent store.
    /// </summary>
    /// <param name="nodeId">The unique identifier of the peer to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveRemotePeerAsync(string nodeId, CancellationToken cancellationToken = default);
}
