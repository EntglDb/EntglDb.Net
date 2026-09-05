using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core.Network;
using Microsoft.Extensions.Logging;

namespace EntglDb.Network;

/// <summary>
/// Default implementation of <see cref="IPeerConnectionPool"/>.
/// Keyed by peer address (<c>host:port</c>); handles connect and handshake lazily.
/// </summary>
public sealed class PeerConnectionPool : IPeerConnectionPool
{
    private readonly Func<string, TcpPeerClient> _factory;
    private readonly IPeerNodeConfigurationProvider _configProvider;
    private readonly ILogger _logger;

    private readonly ConcurrentDictionary<string, TcpPeerClient> _pool = new(StringComparer.Ordinal);

    // One critical section per peer address, held by whichever caller currently owns that peer's
    // connection - from AcquireAsync through the caller's Dispose of the returned lease. This is the
    // ONLY thing that makes a peer's connection safe to share: TcpPeerClient.ConnectAsync/HandshakeAsync
    // guard only their own synchronous state checks with a plain `lock` (which cannot span an `await`),
    // and ProtocolHandler's read/write locks only serialize individual frames, not a caller's whole
    // request-response exchange. Without an outer per-peer lease, two unrelated callers for the same
    // peer (e.g. the sync orchestrator's multi-step vector-clock exchange racing an application service
    // sending its own custom message type right after a dropped connection reconnects) can end up
    // reading and writing the same stream concurrently: their bytes interleave, so one caller's read
    // consumes bytes meant for the other - producing anything from a nonsensical "peer key length" during
    // handshake to a corrupted response deeper into an established session. Keyed per peer (not global)
    // so unrelated peers still connect and sync fully in parallel.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public PeerConnectionPool(
        Func<string, TcpPeerClient> factory,
        IPeerNodeConfigurationProvider configProvider,
        ILogger logger)
    {
        _factory = factory;
        _configProvider = configProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IAsyncDisposable> AcquireAsync(string peerAddress, CancellationToken token = default)
    {
        var gate = _locks.GetOrAdd(peerAddress, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        return new PeerLease(gate);
    }

    /// <inheritdoc/>
    public async Task<TcpPeerClient> GetOrConnectAsync(
        string peerAddress,
        IEnumerable<string>? interestingCollections = null,
        CancellationToken token = default)
    {
        // Callers must hold the AcquireAsync lease for peerAddress before calling this - the exclusivity
        // that used to be enforced here is now the caller's responsibility for its whole session, not
        // just this connect step (see the class remarks on _locks).
        var client = _pool.GetOrAdd(peerAddress, _factory);

        if (!client.IsConnected)
            await client.ConnectAsync(token);

        if (!client.HasHandshaked)
        {
            var config = await _configProvider.GetConfiguration();
            if (!await client.HandshakeAsync(config.NodeId, config.AuthToken, interestingCollections, token))
                throw new InvalidOperationException($"Handshake with peer '{peerAddress}' was rejected.");
        }

        return client;
    }

    /// <inheritdoc/>
    public void Invalidate(string peerAddress)
    {
        if (_pool.TryRemove(peerAddress, out var client))
        {
            try { client.Dispose(); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing invalidated client for {Address}.", peerAddress);
            }
        }
    }

    public void Dispose()
    {
        foreach (var client in _pool.Values)
            try { client.Dispose(); } catch { /* best effort */ }

        _pool.Clear();

        foreach (var gate in _locks.Values)
            try { gate.Dispose(); } catch { /* best effort */ }

        _locks.Clear();
    }

    private sealed class PeerLease : IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate;
        private int _released;

        public PeerLease(SemaphoreSlim gate) => _gate = gate;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                _gate.Release();

            return default;
        }
    }
}
