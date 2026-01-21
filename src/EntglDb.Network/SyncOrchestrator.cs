using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Network.Security;
using EntglDb.Network.Telemetry;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    private readonly EntglDb.Network.Telemetry.INetworkTelemetryService? _telemetry;

    public SyncOrchestrator(
        IDiscoveryService discovery,
        IPeerStore store,
        IPeerNodeConfigurationProvider peerNodeConfigurationProvider,
        ILoggerFactory loggerFactory,
        IPeerHandshakeService? handshakeService = null,
        EntglDb.Network.Telemetry.INetworkTelemetryService? telemetry = null)
    {
        _discovery = discovery;
        _store = store;
        _peerNodeConfigurationProvider = peerNodeConfigurationProvider;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<SyncOrchestrator>();
        _handshakeService = handshakeService;
        _telemetry = telemetry;
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

                // NetStandard 2.0 fallback: Use Task.WhenAll
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
    /// Refactored to handle bi-directional sync (convergence) and cleaner validation.
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
                _handshakeService,
                _telemetry));

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

            // 2. Determine Sync Direction (Bi-directional check for convergence)
            // Note: Standard CompareTo usually returns > 0 if strict, but for VectorClocks we must handle concurrency.
            // We separate the checks to allow PULL and PUSH in the same session if clocks are concurrent.

            bool remoteHasUpdates = remoteClock.CompareTo(localClock) > 0;
            bool localHasUpdates = localClock.CompareTo(remoteClock) > 0;
            bool areConcurrentOrEqual = !remoteHasUpdates && !localHasUpdates;

            // PULL: If remote is strictly ahead OR if we are concurrent (diverged)
            if (remoteHasUpdates || (areConcurrentOrEqual && !remoteClock.Equals(localClock)))
            {
                _logger.LogInformation("Pulling changes from {NodeId} (Remote: {Remote}, Local: {Local})", peer.NodeId, remoteClock, localClock);

                var changes = await client.PullChangesAsync(localClock, token);
                if (changes != null && changes.Count > 0)
                {
                    await ProcessInboundBatchAsync(client, peer.NodeId, changes, token);
                }
            }

            // PUSH: If local is strictly ahead OR if we are concurrent (diverged)
            if (localHasUpdates || (areConcurrentOrEqual && !remoteClock.Equals(localClock)))
            {
                _logger.LogInformation("Pushing changes to {NodeId}", peer.NodeId);
                var changes = await _store.GetOplogAfterAsync(remoteClock, token);
                if (changes != null && changes.Any())
                {
                    await client.PushChangesAsync(changes, token);
                }
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
            if (shouldRemoveClient && client != null)
            {
                if (_clients.TryRemove(peer.NodeId, out var removedClient))
                {
                    try { removedClient.Dispose(); } catch { /* Ignore disposal errors */ }
                }
            }
        }
    }

    /// <summary>
    /// Validates an inbound batch of changes, checks for gaps, performs recovery if needed, and applies to store.
    /// Extracted to enforce Single Responsibility Principle.
    /// </summary>
    private async Task ProcessInboundBatchAsync(TcpPeerClient client, string peerNodeId, IList<OplogEntry> changes, CancellationToken token)
    {
        _logger.LogInformation("Received {Count} changes from {NodeId}", changes.Count, peerNodeId);

        // 1. Validate internal integrity of the batch (Hash check)
        foreach (var entry in changes)
        {
            if (!entry.IsValid())
            {
                throw new InvalidOperationException($"Integrity Check Failed for Entry {entry.Hash} (Node: {entry.Timestamp.NodeId})");
            }
        }

        // 2. Group changes by Author Node to validate Source Chains independently
        var changesByNode = changes.GroupBy(c => c.Timestamp.NodeId);

        foreach (var group in changesByNode)
        {
            var authorNodeId = group.Key;
            var authorChain = group.OrderBy(c => c.Timestamp.LogicalCounter).ToList();

            // Check linkage within the batch
            for (int i = 1; i < authorChain.Count; i++)
            {
                if (authorChain[i].PreviousHash != authorChain[i - 1].Hash)
                {
                    throw new InvalidOperationException($"Chain Broken in Batch for Node {authorNodeId}");
                }
            }

            // Check linkage with Local State
            var firstEntry = authorChain[0];
            var localHeadHash = await _store.GetLastEntryHashAsync(authorNodeId, token);

            if (localHeadHash != null && firstEntry.PreviousHash != localHeadHash)
            {
                // GAP DETECTED
                _logger.LogWarning("Gap Detected for Node {AuthorId}. Local Head: {Local}, Remote Prev: {Prev}. Initiating Recovery.", authorNodeId, localHeadHash, firstEntry.PreviousHash);

                // Gap Recovery (Range Sync)
                var missingChain = await client.GetChainRangeAsync(localHeadHash, firstEntry.PreviousHash, token);

                if (missingChain != null && missingChain.Any())
                {
                    _logger.LogInformation("Gap Recovery: Retrieved {Count} missing entries.", missingChain.Count);

                    // Validate Recovery Chain Linkage
                    if (missingChain[0].PreviousHash != localHeadHash)
                        throw new InvalidOperationException("Recovery Chain does not link to Local Head");

                    for (int i = 1; i < missingChain.Count; i++)
                        if (missingChain[i].PreviousHash != missingChain[i - 1].Hash)
                            throw new InvalidOperationException("Recovery Chain has internal breaks");

                    if (missingChain.Last().Hash != firstEntry.PreviousHash)
                        throw new InvalidOperationException("Recovery Chain does not link to Batch Start");

                    // Apply Missing Chain First
                    await _store.ApplyBatchAsync(Enumerable.Empty<Document>(), missingChain, token);
                    _logger.LogInformation("Gap Recovery Applied Successfully.");
                }
                else
                {
                    // Fail hard or soft depending on policy. Hard fail protects consistency.
                    throw new InvalidOperationException($"Could not recover gap for Node {authorNodeId}. Peer might not have history.");
                }
            }
            else if (localHeadHash == null && !string.IsNullOrEmpty(firstEntry.PreviousHash))
            {
                // Implicit Accept / Partial Sync warning
                _logger.LogWarning("First contact with Node {AuthorId} at explicit state (Not Genesis). Accepting.", authorNodeId);
            }
        }

        // Apply original batch
        await _store.ApplyBatchAsync(Enumerable.Empty<Document>(), changes, token);
    }
}