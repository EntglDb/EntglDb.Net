using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using EntglDb.Core.Network;

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
    private readonly ILocalInterestsProvider? _localInterests;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, PeerNode> _activePeers = new();
    private readonly object _startStopLock = new object();

    public UdpDiscoveryService(
        IPeerNodeConfigurationProvider peerNodeConfigurationProvider,
        ILogger<UdpDiscoveryService> logger,
        ILocalInterestsProvider? localInterests = null)
    {
        _configProvider = peerNodeConfigurationProvider ?? throw new ArgumentNullException(nameof(peerNodeConfigurationProvider));
        _localInterests = localInterests;
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
                    InterestingCollections = _localInterests?.InterestedCollection.ToList() ?? new List<string>()
                };

                var json = JsonSerializer.Serialize(beacon);
                var bytes = Encoding.UTF8.GetBytes(json);

                // Broadcast on each active IPv4 subnet to avoid route selection issues on multi-interface hosts.
                var endpoints = GetBroadcastEndpoints(DiscoveryPort);
                foreach (var endpoint in endpoints)
                {
                    try
                    {
                        await udp.SendAsync(bytes, bytes.Length, endpoint);
                    }
                    catch (SocketException sex) when (sex.SocketErrorCode is SocketError.HostUnreachable or SocketError.NetworkUnreachable)
                    {
                        _logger.LogWarning(sex, "UDP Broadcast route unavailable for {Endpoint}", endpoint);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UDP Broadcast Error");
            }

            await Task.Delay(5000, token);
        }
    }

    private static IEnumerable<IPEndPoint> GetBroadcastEndpoints(int port)
    {
        var endpoints = new HashSet<IPAddress>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            IPInterfaceProperties? properties;
            try
            {
                properties = nic.GetIPProperties();
            }
            catch
            {
                continue;
            }

            foreach (var unicast in properties.UnicastAddresses)
            {
                var ip = unicast.Address;
                var mask = unicast.IPv4Mask;

                if (ip.AddressFamily != AddressFamily.InterNetwork || mask == null)
                {
                    continue;
                }

                if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any))
                {
                    continue;
                }

                endpoints.Add(ComputeBroadcastAddress(ip, mask));
            }
        }

        if (endpoints.Count == 0)
        {
            endpoints.Add(IPAddress.Broadcast);
        }

        return endpoints.Select(address => new IPEndPoint(address, port));
    }

    private static IPAddress ComputeBroadcastAddress(IPAddress address, IPAddress mask)
    {
        var ipBytes = address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        var broadcastBytes = new byte[ipBytes.Length];

        for (var i = 0; i < ipBytes.Length; i++)
        {
            broadcastBytes[i] = (byte)(ipBytes[i] | (~maskBytes[i] & 0xFF));
        }

        return new IPAddress(broadcastBytes);
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
