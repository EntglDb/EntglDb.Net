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

namespace EntglDb.Network
{
    /// <summary>
    /// Provides UDP-based peer discovery for the EntglDb network.
    /// Broadcasts presence beacons and listens for other nodes on the local network.
    /// </summary>
    public class UdpDiscoveryService
    {
        private const int DiscoveryPort = 5000;

        private readonly ILogger<UdpDiscoveryService> _logger;
        private readonly bool _useLocalhost;

        private readonly string _nodeId;

        public int TcpPort { get; set; }
        private CancellationTokenSource? _cts;
        private readonly ConcurrentDictionary<string, PeerNode> _activePeers = new();

        public UdpDiscoveryService(string nodeId, int tcpPort, ILogger<UdpDiscoveryService> logger, bool useLocalhost = false)
        {
            _nodeId = nodeId;
            TcpPort = tcpPort;
            _logger = logger;
            _useLocalhost = useLocalhost;
        }

        /// <summary>
        /// Starts the discovery service, initiating listener, broadcaster, and cleanup tasks.
        /// </summary>
        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            
            Task.Run(() => ListenAsync(_cts.Token));
            Task.Run(() => BroadcastAsync(_cts.Token));
            Task.Run(() => CleanupAsync(_cts.Token));
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
            var targetAddress = _useLocalhost ? IPAddress.Loopback : address;
            var endpoint = $"{targetAddress}:{beacon.TcpPort}";
            
            var peer = new PeerNode(peerId, endpoint, DateTimeOffset.UtcNow);
            
            _activePeers.AddOrUpdate(peerId, peer, (key, old) => peer);

            // Log strictly new peers or re-discovered ones? 
            // AddOrUpdate is atomic.
            // Logging "Discovered" every 5s is noisy. We should only log if it's NEW.
            // But ConcurrentDictionary doesn't tell us if it was update or add easily without tricky logic or Compare.
            // For now, let's just rely on the fact that existing logic logged only on Add.
            // I'll stick to AddOrUpdate but if we want logs we need to check existence.
            // Actually, `TryAdd` was used before.
            // If I use `AddOrUpdate`, I overwrite.
            // Let's do:
            /*
            if (_activePeers.ContainsKey(peerId)) {
                _activePeers[peerId] = peer; // Update timestamp
            } else {
                 if (_activePeers.TryAdd(peerId, peer)) log...
            }
            */
            // But that's racey. AddOrUpdate is better.
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
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
                        var beacon = JsonSerializer.Deserialize<DiscoveryBeacon>(json);
                        if (beacon != null && beacon.NodeId != _nodeId)
                        {
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
            
            var beacon = new DiscoveryBeacon { NodeId = _nodeId, TcpPort = TcpPort };
            var json = JsonSerializer.Serialize(beacon);
            var bytes = Encoding.UTF8.GetBytes(json);
            var endpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);

            _logger.LogInformation("UDP Broadcasting started for {NodeId}", _nodeId);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await udp.SendAsync(bytes, bytes.Length, endpoint);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "UDP Broadcast Error");
                }

                await Task.Delay(5000, token);
            }
        }



        private class DiscoveryBeacon
        {
            public string NodeId { get; set; } = "";
            public int TcpPort { get; set; }
        }
    }
}
