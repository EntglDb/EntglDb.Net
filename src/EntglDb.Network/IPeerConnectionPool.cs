using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Network;

/// <summary>
/// Manages a pool of <see cref="TcpPeerClient"/> connections, one per peer address.
/// Connections are created, connected, and handshaked lazily on first use.
/// All services that need outbound connections share this single pool, so they
/// reuse the same underlying TCP socket to each peer.
/// </summary>
/// <remarks>Call <see cref="Invalidate"/> to evict and dispose a connection after an error.</remarks>
public interface IPeerConnectionPool : IDisposable
{
    /// <summary>
    /// Returns an existing connected and handshaked client for <paramref name="peerAddress"/>,
    /// or creates, connects, and handshakes a new one.
    /// </summary>
    /// <param name="peerAddress">The remote peer address in <c>host:port</c> format.</param>
    /// <param name="interestingCollections">
    /// Collections to advertise during handshake. Pass <c>null</c> for generic (non-sync) usage.
    /// </param>
    /// <param name="token">Cancellation token.</param>
    Task<TcpPeerClient> GetOrConnectAsync(
        string peerAddress,
        IEnumerable<string>? interestingCollections = null,
        CancellationToken token = default);

    /// <summary>
    /// Removes and disposes the client for <paramref name="peerAddress"/> so that the next
    /// <see cref="GetOrConnectAsync"/> call recreates and reconnects it.
    /// </summary>
    void Invalidate(string peerAddress);
}
