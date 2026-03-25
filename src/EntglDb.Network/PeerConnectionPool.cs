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
    public async Task<TcpPeerClient> GetOrConnectAsync(
        string peerAddress,
        IEnumerable<string>? interestingCollections = null,
        CancellationToken token = default)
    {
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
    }
}
