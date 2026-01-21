using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Network.Security;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Network;

/// <summary>
/// Orchestrates the synchronization process between the local node and discovered peers.
/// Manages anti-entropy sessions and data exchange.
/// </summary>
public class SyncOrchestrator : ISyncOrchestrator
{
    private readonly IDiscoveryService _discovery;
    private readonly IPeerStore _store;
    private readonly IPeerNodeConfigurationProvider _peerNodeConfigurationProvider;
    private readonly ILogger<SyncOrchestrator> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private CancellationTokenSource? _cts;
    private readonly Random _random = new Random();
    private readonly object _startStopLock = new object();

    // Persistent clients pool
    private readonly ConcurrentDictionary<string, TcpPeerClient> _clients = new();

    private readonly IPeerHandshakeService? _handshakeService;

    public SyncOrchestrator(
        IDiscoveryService discovery,
        IPeerStore store,
        IPeerNodeConfigurationProvider peerNodeConfigurationProvider,
        ILoggerFactory loggerFactory,
        IPeerHandshakeService? handshakeService = null)
    {
        _discovery = discovery;
        _store = store;
        _peerNodeConfigurationProvider = peerNodeConfigurationProvider;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<SyncOrchestrator>();
        _handshakeService = handshakeService;
    }

    public async Task Start()
    {
        lock (_startStopLock)
        {
            if (_cts != null)
            {
                _logger.LogWarning("Sync Orchestrator already started");
                return;
            }
            _cts = new CancellationTokenSource();
        }

        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await SyncLoopAsync(token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync Loop task failed");
            }
        }, token);
        
        await Task.CompletedTask;
    }

    public async Task Stop()
    {
        CancellationTokenSource? ctsToDispose = null;
        
        lock (_startStopLock)
        {
            if (_cts == null)
            {
                _logger.LogWarning("Sync Orchestrator already stopped or never started");
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
        
        // Cleanup clients
        foreach (var client in _clients.Values)
        {
            try
            {
                client.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing client during shutdown");
            }
        }
        _clients.Clear();
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Main synchronization loop. Periodically selects random peers to gossip with.
    /// </summary>
    private async Task SyncLoopAsync(CancellationToken token)
    {
        _logger.LogInformation("Sync Orchestrator Started (Parallel P2P)");
        while (!token.IsCancellationRequested)
        {
            var config = await _peerNodeConfigurationProvider.GetConfiguration();
            try
            {
                var peers = _discovery.GetActivePeers().Where(p => p.NodeId != config.NodeId).ToList();

                // Gossip Fanout: Pick 3 random peers
                var targets = peers.OrderBy(x => _random.Next()).Take(3).ToList();

                // Execute sync in parallel with a max degree of parallelism
//#if NET6_0_OR_GREATER
//                await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = token }, async (peer, ct) => 
//                {
//                    await TrySyncWithPeer(peer, ct);
//                });
//#else
//                // NetStandard 2.0 fallback: Use Task.WhenAll (Since we only take 3 targets, this effectively runs them in parallel)
//                var tasks = targets.Select(peer => TrySyncWithPeer(peer, token));
//                await Task.WhenAll(tasks);
//#endif
                // NetStandard 2.0 fallback: Use Task.WhenAll (Since we only take 3 targets, this effectively runs them in parallel)
                var tasks = targets.Select(peer => TrySyncWithPeer(peer, token));
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Sync Loop Cancelled");
                break; 
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync Loop Error");
            }

            try
            {
                await Task.Delay(2000, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Attempts to synchronize with a specific peer.
    /// Performs handshake, clock comparison, and data exchange (Push/Pull).
    /// </summary>
    private async Task TrySyncWithPeer(PeerNode peer, CancellationToken token)
    {
        TcpPeerClient? client = null;
        bool shouldRemoveClient = false;

        try
        {
            var config = await _peerNodeConfigurationProvider.GetConfiguration();
            
            // Get or create persistent client
            client = _clients.GetOrAdd(peer.NodeId, id => new TcpPeerClient(
                peer.Address,
                _loggerFactory.CreateLogger<TcpPeerClient>(),
                _handshakeService));

            // Reconnect if disconnected
            if (!client.IsConnected)
            {
                await client.ConnectAsync(token);
            }

            // Handshake (idempotent)
            if (!await client.HandshakeAsync(config.NodeId, config.AuthToken, token))
            {
                _logger.LogWarning("Handshake rejected by {NodeId}", peer.NodeId);
                shouldRemoveClient = true;
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
        catch (TimeoutException tex)
        {
            _logger.LogWarning("Sync with {NodeId} timed out: {Message}. Will retry later.", peer.NodeId, tex.Message);
            shouldRemoveClient = true;
        }
        catch (SocketException sex)
        {
            _logger.LogWarning("Network error syncing with {NodeId}: {Message}. Will retry later.", peer.NodeId, sex.Message);
            shouldRemoveClient = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Sync failed with {NodeId}: {Message}. Resetting connection.", peer.NodeId, ex.Message);
            shouldRemoveClient = true;
        }
        finally
        {
            // On failure or auth rejection, remove from pool to force fresh connection next time
            if (shouldRemoveClient && client != null)
            {
                if (_clients.TryRemove(peer.NodeId, out var removedClient))
                {
                    try
                    {
                        removedClient.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error disposing client for {NodeId}", peer.NodeId);
                    }
                }
            }
        }
    }
}
