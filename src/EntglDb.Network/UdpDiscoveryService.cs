using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using EntglDb.Core.Network;
using EntglDb.Core;

namespace EntglDb.Network;

/// <summary>
/// Provides UDP-based peer discovery for the EntglDb network.
/// Broadcasts presence beacons and listens for other nodes on the local network.
/// </summary>
internal class UdpDiscoveryService : IDiscoveryService
{
    private const int DiscoveryPort = 25000;
    private readonly ILogger<UdpDiscoveryService> _logger;
    private readonly IPeerNodeConfigurationProvider _configProvider;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, PeerNode> _activePeers = new();
    private readonly object _startStopLock = new object();

    /// <summary>
    /// Initializes a new instance of the UdpDiscoveryService class with the specified peer node configuration provider
    /// and logger.
    /// </summary>
    /// <param name="peerNodeConfigurationProvider">The provider used to retrieve configuration settings for peer nodes. Cannot be null.</param>
    /// <param name="logger">The logger used to record diagnostic and operational information. Cannot be null.</param>
    public UdpDiscoveryService(IPeerNodeConfigurationProvider peerNodeConfigurationProvider, ILogger<UdpDiscoveryService> logger)
    {
        _configProvider = peerNodeConfigurationProvider ?? throw new ArgumentNullException(nameof(peerNodeConfigurationProvider));
        _logger = logger;
    }

    /// <summary>
    /// Starts the discovery service, initiating listener, broadcaster, and cleanup tasks.
    /// </summary>
    public async Task Start()
    {
        lock (_startStopLock)
        {
            if (_cts != null)
            {
                _logger.LogWarning("UDP Discovery Service already started");
                return;
            }
            _cts = new CancellationTokenSource();
        }

        var token = _cts.Token;
        
        _ = Task.Run(async () =>
        {
            try
            {
                await ListenAsync(token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UDP Listen task failed");
            }
        }, token);
        
        _ = Task.Run(async () =>
        {
            try
            {
                await BroadcastAsync(token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UDP Broadcast task failed");
            }
        }, token);
        
        _ = Task.Run(async () =>
        {
            try
            {
                await CleanupAsync(token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UDP Cleanup task failed");
            }
        }, token);

        await Task.CompletedTask;
    }

    // ... Stop ...

    private async Task CleanupAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(10000, token); // Check every 10s
                var now = DateTimeOffset.UtcNow;
                var expired = new List<string>();

                foreach (var pair in _activePeers)
                {
                    // Expiry: 15 seconds (broadcast is every 5s, so 3 missed beats = dead)
                    if ((now - pair.Value.LastSeen).TotalSeconds > 15)
                    {
                        expired.Add(pair.Key);
                    }
                }

                foreach (var id in expired)
                {
                    if (_activePeers.TryRemove(id, out var removed))
                    {
                        _logger.LogInformation("Peer Expired: {NodeId} at {Endpoint}", removed.NodeId, removed.Address);
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cleanup Loop Error");
            }
        }
    }

    // ... Listen ...

    private void HandleBeacon(DiscoveryBeacon beacon, IPAddress address)
    {
        var peerId = beacon.NodeId;
        var endpoint = $"{address}:{beacon.TcpPort}";

        var peer = new PeerNode(peerId, endpoint, DateTimeOffset.UtcNow, interestingCollections: beacon.InterestingCollections);

        _activePeers.AddOrUpdate(peerId, peer, (key, old) => peer);
    }

    public async Task Stop()
    {
        CancellationTokenSource? ctsToDispose = null;
        
        lock (_startStopLock)
        {
            if (_cts == null)
            {
                _logger.LogWarning("UDP Discovery Service already stopped or never started");
                return;
            }
            
            ctsToDispose = _cts;
            _cts = null;
        }
        
        try
        {
            ctsToDispose.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed, ignore
        }
        finally
        {
            ctsToDispose.Dispose();
        }
        
        await Task.CompletedTask;
    }

    public IEnumerable<PeerNode> GetActivePeers() => _activePeers.Values;

    private async Task ListenAsync(CancellationToken token)
    {
        using var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

        _logger.LogInformation("UDP Discovery Listening on port {Port}", DiscoveryPort);

        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync();
                var json = Encoding.UTF8.GetString(result.Buffer);

                try
                {
                    var config = await _configProvider.GetConfiguration();
                    var _nodeId = config.NodeId;
                    var localClusterHash = ComputeClusterHash(config.AuthToken);

                    var beacon = JsonSerializer.Deserialize<DiscoveryBeacon>(json);
                    
                    if (beacon != null && beacon.NodeId != _nodeId)
                    {
                        // Filter by ClusterHash to reduce congestion from different clusters
                        if (!string.Equals(beacon.ClusterHash, localClusterHash, StringComparison.Ordinal))
                        {
                            // Optional: Log trace if needed, but keeping it silent avoids flooding logs during congestion
                            continue; 
                        }

                        HandleBeacon(beacon, result.RemoteEndPoint.Address);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse beacon from {Address}", result.RemoteEndPoint.Address);
                }
            }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UDP Listener Error");
            }
        }
    }

    private async Task BroadcastAsync(CancellationToken token)
    {
        using var udp = new UdpClient();
        udp.EnableBroadcast = true;

        var endpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);

        while (!token.IsCancellationRequested)
        {
            try
            {
                // Re-fetch config each time in case it changes (though usually static)
                var conf = await _configProvider.GetConfiguration();
                
                var beacon = new DiscoveryBeacon 
                { 
                    NodeId = conf.NodeId, 
                    TcpPort = conf.TcpPort,
                    ClusterHash = ComputeClusterHash(conf.AuthToken),
                    InterestingCollections = conf.InterestingCollections ?? new List<string>()
                };

                var json = JsonSerializer.Serialize(beacon);
                var bytes = Encoding.UTF8.GetBytes(json);

                await udp.SendAsync(bytes, bytes.Length, endpoint);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UDP Broadcast Error");
            }

            await Task.Delay(5000, token);
        }
    }

    private string ComputeClusterHash(string authToken)
    {
        if (string.IsNullOrEmpty(authToken)) return "";
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(authToken);
        var hash = sha256.ComputeHash(bytes);
        // Return first 8 chars (4 bytes hex) is enough for filtering
        return BitConverter.ToString(hash).Replace("-", "").Substring(0, 8);
    }



    private class DiscoveryBeacon
    {
        [System.Text.Json.Serialization.JsonPropertyName("node_id")]
        public string NodeId { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("tcp_port")]
        public int TcpPort { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("cluster_hash")]
        public string ClusterHash { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("interests")]
        public List<string> InterestingCollections { get; set; } = new();
    }
}
