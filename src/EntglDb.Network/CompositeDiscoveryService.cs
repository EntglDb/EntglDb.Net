using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Network;

/// <summary>
/// Composite discovery service that combines UDP LAN discovery with persistent remote peers from the database.
/// Periodically refreshes the remote peer list and merges with actively discovered LAN peers.
/// </summary>
public class CompositeDiscoveryService : IDiscoveryService
{
    private readonly IDiscoveryService _udpDiscovery;
    private readonly IPeerStore _peerStore;
    private readonly ILogger<CompositeDiscoveryService> _logger;
    private readonly TimeSpan _refreshInterval;

    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, PeerNode> _remotePeers = new();

    /// <summary>
    /// Initializes a new instance of the CompositeDiscoveryService class.
    /// </summary>
    /// <param name="udpDiscovery">UDP-based LAN discovery service.</param>
    /// <param name="peerStore">Store for retrieving persistent remote peer configurations.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="refreshInterval">Interval for refreshing remote peers from database. Defaults to 5 minutes.</param>
    public CompositeDiscoveryService(
        IDiscoveryService udpDiscovery,
        IPeerStore peerStore,
        ILogger<CompositeDiscoveryService>? logger = null,
        TimeSpan? refreshInterval = null)
    {
        _udpDiscovery = udpDiscovery ?? throw new ArgumentNullException(nameof(udpDiscovery));
        _peerStore = peerStore ?? throw new ArgumentNullException(nameof(peerStore));
        _logger = logger ?? NullLogger<CompositeDiscoveryService>.Instance;
        _refreshInterval = refreshInterval ?? TimeSpan.FromMinutes(5);
    }

    public IEnumerable<PeerNode> GetActivePeers()
    {
        // Merge LAN peers from UDP discovery with remote peers from database
        var lanPeers = _udpDiscovery.GetActivePeers();
        var remotePeers = _remotePeers.Values;

        return lanPeers.Concat(remotePeers);
    }

    public async Task Start()
    {
        if (_cts != null)
        {
            _logger.LogWarning("Composite discovery service already started");
            return;
        }

        // Start UDP discovery
        await _udpDiscovery.Start();

        // Start remote peer refresh loop
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => RefreshLoopAsync(_cts.Token));

        // Initial load of remote peers
        await RefreshRemotePeersAsync();

        _logger.LogInformation("Composite discovery service started (UDP + Remote Peers)");
    }

    public async Task Stop()
    {
        if (_cts == null) return;

        _cts.Cancel();
        _cts.Dispose();
        _cts = null;

        await _udpDiscovery.Stop();

        _logger.LogInformation("Composite discovery service stopped");
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_refreshInterval, cancellationToken);
                await RefreshRemotePeersAsync();
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during remote peer refresh");
            }
        }
    }

    private async Task RefreshRemotePeersAsync()
    {
        try
        {
            var remoteConfigs = await _peerStore.GetRemotePeersAsync();
            var now = DateTimeOffset.UtcNow;

            // Update remote peers dictionary
            _remotePeers.Clear();

            foreach (var config in remoteConfigs.Where(c => c.IsEnabled))
            {
                var peerNode = new PeerNode(
                    config.NodeId,
                    config.Address,
                    now, // LastSeen is now for persistent peers (always considered active)
                    config.Type,
                    NodeRole.Member // Remote peers are always members, never gateways
                );

                _remotePeers[config.NodeId] = peerNode;
            }

            _logger.LogInformation("Refreshed remote peers: {Count} enabled peers loaded", _remotePeers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh remote peers from database");
        }
    }
}
