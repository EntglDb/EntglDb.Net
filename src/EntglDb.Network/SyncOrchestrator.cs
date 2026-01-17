using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Logging;
using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Core.Network;
using EntglDb.Network.Security;

namespace EntglDb.Network
{
    /// <summary>
    /// Orchestrates the synchronization process between the local node and discovered peers.
    /// Manages anti-entropy sessions and data exchange.
    /// </summary>
    public class SyncOrchestrator
    {
        private readonly UdpDiscoveryService _discovery;
        private readonly IPeerStore _store;
        private readonly string _nodeId;
        private readonly ILogger<SyncOrchestrator> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private CancellationTokenSource? _cts;
        private readonly Random _random = new Random();

        // Persistent clients pool
        private readonly ConcurrentDictionary<string, TcpPeerClient> _clients = new();

        private readonly string _authToken;
        private readonly IPeerHandshakeService? _handshakeService;

        public SyncOrchestrator(
            UdpDiscoveryService discovery, 
            IPeerStore store, 
            string nodeId, 
            string authToken,
            ILoggerFactory loggerFactory,
            IPeerHandshakeService? handshakeService = null)
        {
            _discovery = discovery;
            _store = store;
            _nodeId = nodeId;
            _authToken = authToken;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<SyncOrchestrator>();
            _handshakeService = handshakeService;
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            Task.Run(() => SyncLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            // Cleanup clients
            foreach(var client in _clients.Values) client.Dispose();
            _clients.Clear();
        }

        /// <summary>
        /// Main synchronization loop. Periodically selects random peers to gossip with.
        /// </summary>
        private async Task SyncLoopAsync(CancellationToken token)
        {
            _logger.LogInformation("Sync Orchestrator Started (Parallel P2P)");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var peers = _discovery.GetActivePeers().Where(p => p.NodeId != _nodeId).ToList();
                    
                    // Gossip Fanout: Pick 3 random peers
                    var targets = peers.OrderBy(x => _random.Next()).Take(3).ToList();

                    // Execute sync in parallel with a max degree of parallelism
#if NET6_0_OR_GREATER
                    await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = token }, async (peer, ct) => 
                    {
                        await TrySyncWithPeer(peer, ct);
                    });
#else
                    // NetStandard 2.0 fallback: Use Task.WhenAll (Since we only take 3 targets, this effectively runs them in parallel)
                    var tasks = targets.Select(peer => TrySyncWithPeer(peer, token));
                    await Task.WhenAll(tasks);
#endif
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Sync Loop Error");
                }

                await Task.Delay(2000, token); 
            }
        }

        /// <summary>
        /// Attempts to synchronize with a specific peer.
        /// Performs handshake, clock comparison, and data exchange (Push/Pull).
        /// </summary>
        private async Task TrySyncWithPeer(PeerNode peer, CancellationToken token)
        {
            TcpPeerClient? client = null;

            try
            {
                // Get or create persistent client
                client = _clients.GetOrAdd(peer.NodeId, id => new TcpPeerClient(
                    peer.Address, 
                    _loggerFactory.CreateLogger<TcpPeerClient>(), 
                    _handshakeService));

                await client.ConnectAsync(token);

                // Handshake (idempotent)
                if (!await client.HandshakeAsync(_nodeId, _authToken, token))
                {
                    _logger.LogWarning("Handshake rejected by {NodeId}", peer.NodeId);
                    return;
                }

                // 1. Anti-Entropy (Get Clock)
                var remoteClock = await client.GetClockAsync(token);
                var localClock = await _store.GetLatestTimestampAsync(token); 

                // 2. Determine Sync Direction
                if (remoteClock.CompareTo(localClock) > 0)
                {
                    // Remote is ahead -> Pull
                    _logger.LogInformation("Pulling changes from {NodeId} (Remote: {Remote}, Local: {Local})", peer.NodeId, remoteClock, localClock);
                    var changes = await client.PullChangesAsync(localClock, token);
                    if (changes.Count > 0)
                    {
                        _logger.LogInformation("Received {Count} changes from {NodeId}", changes.Count, peer.NodeId);
                        await _store.ApplyBatchAsync(System.Linq.Enumerable.Empty<Document>(), changes, token);
                    }
                }
                else if (localClock.CompareTo(remoteClock) > 0)
                {
                    // Local is ahead -> Push (Optimistic)
                     _logger.LogInformation("Pushing changes to {NodeId}", peer.NodeId);
                     var changes = await _store.GetOplogAfterAsync(remoteClock, token);
                     await client.PushChangesAsync(changes, token);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Sync failed with {NodeId}: {Message}. Resetting connection.", peer.NodeId, ex.Message);
                
                // On failure, remove from pool to force reconnection next time
                if (client != null)
                {
                    _clients.TryRemove(peer.NodeId, out _);
                    client.Dispose();
                }
            }
        }
    }
}
