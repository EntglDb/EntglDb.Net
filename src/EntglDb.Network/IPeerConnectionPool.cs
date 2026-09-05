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
    /// Acquires exclusive use of the connection to <paramref name="peerAddress"/> for the duration of
    /// the returned lease. Callers MUST hold this lease for their entire exchange with the peer - from
    /// the <see cref="GetOrConnectAsync"/> call through the last send/receive of that logical session -
    /// and dispose it when done (an <c>await using</c> block is the natural fit).
    /// </summary>
    /// <remarks>
    /// Without this, two unrelated callers (e.g. the sync orchestrator running a multi-step vector-clock
    /// exchange, and an application service sending its own custom message type) could both obtain the
    /// same pooled <see cref="TcpPeerClient"/> and read/write it concurrently: their bytes interleave on
    /// the shared stream, so one caller's read can consume bytes meant for the other, corrupting framing
    /// for both. A single per-peer lease serializes every caller's full session with that peer, including
    /// the initial connect+handshake if one is needed.
    /// </remarks>
    /// <param name="peerAddress">The remote peer address in <c>host:port</c> format.</param>
    /// <param name="token">Cancellation token.</param>
    Task<IAsyncDisposable> AcquireAsync(string peerAddress, CancellationToken token = default);

    /// <summary>
    /// Returns an existing connected and handshaked client for <paramref name="peerAddress"/>,
    /// or creates, connects, and handshakes a new one. The caller must already hold the lease from
    /// <see cref="AcquireAsync"/> for this <paramref name="peerAddress"/> before calling this.
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
