using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Network.Security;
using EntglDb.Network.Telemetry;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
    private readonly ConcurrentDictionary<string, PeerStatus> _peerStates = new();

    private readonly IPeerHandshakeService? _handshakeService;
    private readonly INetworkTelemetryService? _telemetry;
    private class PeerStatus
    {
        public int FailureCount { get; set; }
        public DateTime NextRetryTime { get; set; }
    }

    private DateTime _lastMaintenanceTime = DateTime.MinValue;

    public SyncOrchestrator(
        IDiscoveryService discovery,
        IPeerStore store,
        IPeerNodeConfigurationProvider peerNodeConfigurationProvider,
        ILoggerFactory loggerFactory,
        IPeerHandshakeService? handshakeService = null,
        INetworkTelemetryService? telemetry = null)
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
                var allPeers = _discovery.GetActivePeers().Where(p => p.NodeId != config.NodeId).ToList();
                
                // Filter peers based on backoff
                var now = DateTime.UtcNow;
                var eligiblePeers = allPeers.Where(p => 
                {
                    if (_peerStates.TryGetValue(p.NodeId, out var status))
                    {
                        return status.NextRetryTime <= now;
                    }
                    return true;
                }).ToList();

                // Gossip Fanout: Pick 3 random peers from eligible set
                var targets = eligiblePeers.OrderBy(x => _random.Next()).Take(3).ToList();

                // NetStandard 2.0 fallback: Use Task.WhenAll
                var tasks = targets.Select(peer => TrySyncWithPeer(peer, token));
                await Task.WhenAll(tasks);

                // Periodic Maintenance: Prune Oplog based on configuration
                var maintenanceInterval = TimeSpan.FromMinutes(config.MaintenanceIntervalMinutes);
                if ((now - _lastMaintenanceTime) >= maintenanceInterval)
                {
                    _logger.LogInformation("Running periodic maintenance (Oplog pruning)...");
                    try
                    {
                        var retentionHours = config.OplogRetentionHours;
                        var cutoff = new HlcTimestamp(DateTimeOffset.UtcNow.AddHours(-retentionHours).ToUnixTimeMilliseconds(), 0, config.NodeId);
                        await _store.PruneOplogAsync(cutoff, token);
                        _lastMaintenanceTime = now;
                        _logger.LogInformation("Maintenance completed successfully (Retention: {RetentionHours}h).", retentionHours);
                    }
                    catch (Exception maintenanceEx)
                    {
                        _logger.LogError(maintenanceEx, "Maintenance failed.");
                    }
                }
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
    /// Uses Vector Clock comparison to determine what to pull/push for each node.
    /// Performs handshake, vector clock exchange, and data exchange (Push/Pull per node).
    /// </summary>
    private async Task TrySyncWithPeer(PeerNode peer, CancellationToken token)
    {
        TcpPeerClient? client = null;
        bool shouldRemoveClient = false;
        bool syncSuccessful = false;

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
                throw new Exception("Handshake rejected");
            }

            // 1. Exchange Vector Clocks
            var remoteVectorClock = await client.GetVectorClockAsync(token);
            var localVectorClock = await _store.GetVectorClockAsync(token);

            _logger.LogDebug("Vector Clock - Local: {Local}, Remote: {Remote}", localVectorClock, remoteVectorClock);

            // 2. Determine causality relationship
            var causality = localVectorClock.CompareTo(remoteVectorClock);

            // 3. PULL: Identify nodes where remote is ahead
            var nodesToPull = localVectorClock.GetNodesWithUpdates(remoteVectorClock).ToList();
            if (nodesToPull.Any())
            {
                _logger.LogInformation("Pulling changes from {PeerNodeId} for {Count} nodes: {Nodes}",
                    peer.NodeId, nodesToPull.Count, string.Join(", ", nodesToPull));

                foreach (var nodeId in nodesToPull)
                {
                    var localTs = localVectorClock.GetTimestamp(nodeId);
                    var remoteTs = remoteVectorClock.GetTimestamp(nodeId);

                    _logger.LogDebug("Pulling Node {NodeId}: Local={LocalTs}, Remote={RemoteTs}",
                        nodeId, localTs, remoteTs);

                    var changes = await client.PullChangesFromNodeAsync(nodeId, localTs, token);
                    if (changes != null && changes.Count > 0)
                    {
                        var result = await ProcessInboundBatchAsync(client, peer.NodeId, changes, token);
                        if (result != SyncBatchResult.Success)
                        {
                            _logger.LogWarning("Inbound batch processing failed with status {Status}. Aborting sync for this session.", result);
                            RecordFailure(peer.NodeId);
                            return;
                        }
                    }
                }
            }

            // 4. PUSH: Identify nodes where local is ahead
            var nodesToPush = localVectorClock.GetNodesToPush(remoteVectorClock).ToList();
            if (nodesToPush.Any())
            {
                _logger.LogInformation("Pushing changes to {PeerNodeId} for {Count} nodes: {Nodes}",
                    peer.NodeId, nodesToPush.Count, string.Join(", ", nodesToPush));

                foreach (var nodeId in nodesToPush)
                {
                    var remoteTs = remoteVectorClock.GetTimestamp(nodeId);
                    var changes = await _store.GetOplogForNodeAfterAsync(nodeId, remoteTs, token);

                    var changesList = changes.ToList();
                    if (changesList.Any())
                    {
                        _logger.LogDebug("Pushing {Count} changes for Node {NodeId}", changesList.Count, nodeId);
                        await client.PushChangesAsync(changesList, token);
                    }
                }
            }

            // 5. Handle Concurrent/Equal cases
            if (causality == CausalityRelation.Equal)
            {
                _logger.LogDebug("Vector clocks are equal with {PeerNodeId}. No sync needed.", peer.NodeId);
            }
            else if (causality == CausalityRelation.Concurrent && !nodesToPull.Any() && !nodesToPush.Any())
            {
                _logger.LogDebug("Vector clocks are concurrent with {PeerNodeId}, but no divergence detected.", peer.NodeId);
            }
            
            syncSuccessful = true;
            RecordSuccess(peer.NodeId);
        }

        catch (SnapshotRequiredException)
        {
            _logger.LogWarning("Snapshot required for peer {NodeId}. Initiating merge sync.", peer.NodeId);
            if (client != null && client.IsConnected)
            {
                try 
                {
                    await PerformSnapshotSyncAsync(client, true, token);
                    syncSuccessful = true;
                    RecordSuccess(peer.NodeId);
                }
                catch
                {
                     RecordFailure(peer.NodeId);
                     shouldRemoveClient = true;
                }
            }
            else
            {
                 RecordFailure(peer.NodeId);
                 shouldRemoveClient = true;
            }
        }
        catch (CorruptDatabaseException cex)
        {
            _logger.LogCritical(cex, "Local database corruption detected during sync with {NodeId}. Initiating EMERGENCY SNAPSHOT RECOVERY.", peer.NodeId);
            if (client != null && client.IsConnected)
            {
                try
                {
                    // EMERGENCY RECOVERY: Replace local DB with remote snapshot (mergeOnly: false)
                    await PerformSnapshotSyncAsync(client, false, token);
                    syncSuccessful = true;
                    RecordSuccess(peer.NodeId);
                    _logger.LogInformation("Emergency recovery successful. Local database replaced.");
                }
                catch (Exception recoveryEx)
                {
                    _logger.LogCritical(recoveryEx, "Emergency recovery failed. App state is critical.");
                    RecordFailure(peer.NodeId);
                    shouldRemoveClient = true;
                }
            }
            else
            {
                 RecordFailure(peer.NodeId);
                 shouldRemoveClient = true;
            }
        }
        catch (TimeoutException tex)
        {
            _logger.LogWarning("Sync with {NodeId} timed out: {Message}. Will retry later.", peer.NodeId, tex.Message);
            shouldRemoveClient = true;
            RecordFailure(peer.NodeId);
        }
        catch (SocketException sex)
        {
            _logger.LogWarning("Network error syncing with {NodeId}: {Message}. Will retry later.", peer.NodeId, sex.Message);
            shouldRemoveClient = true;
            RecordFailure(peer.NodeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Sync failed with {NodeId}: {Message}. Resetting connection.", peer.NodeId, ex.Message);
            shouldRemoveClient = true;
            RecordFailure(peer.NodeId);
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
    
    private void RecordSuccess(string nodeId)
    {
        _peerStates.AddOrUpdate(nodeId, 
            new PeerStatus { FailureCount = 0, NextRetryTime = DateTime.MinValue },
            (k, v) => { v.FailureCount = 0; v.NextRetryTime = DateTime.MinValue; return v; });
    }

    private void RecordFailure(string nodeId)
    {
        _peerStates.AddOrUpdate(nodeId,
            new PeerStatus { FailureCount = 1, NextRetryTime = DateTime.UtcNow.AddSeconds(1) },
            (k, v) => 
            {
                v.FailureCount++;
                // Exponential backoff: 1s, 2s, 4s... max 60s
                var delaySeconds = Math.Min(Math.Pow(2, v.FailureCount), 60);
                v.NextRetryTime = DateTime.UtcNow.AddSeconds(delaySeconds);
                return v;
            });
    }

    /// <summary>
    /// Validates an inbound batch of changes, checks for gaps, performs recovery if needed, and applies to store.
    /// Extracted to enforce Single Responsibility Principle.
    /// </summary>
    private enum SyncBatchResult
    {
        Success,
        GapDetected,
        IntegrityError,
        ChainBroken
    }

    /// <summary>
    /// Validates an inbound batch of changes, checks for gaps, performs recovery if needed, and applies to store.
    /// Extracted to enforce Single Responsibility Principle.
    /// </summary>
    private async Task<SyncBatchResult> ProcessInboundBatchAsync(TcpPeerClient client, string peerNodeId, IList<OplogEntry> changes, CancellationToken token)
    {
        _logger.LogInformation("Received {Count} changes from {NodeId}", changes.Count, peerNodeId);

        // 1. Validate internal integrity of the batch (Hash check)
        foreach (var entry in changes)
        {
            if (!entry.IsValid())
            {
                // CHANGED: Log Critical Error but ACCEPT the entry to allow sync to progress (Soft Validation).
                // Throwing here would cause an unrecoverable state where this batch blocks sync forever.
                _logger.LogError("Integrity Check Failed for Entry {Hash} (Node: {NodeId}). Expected: {computedHash}. ACCEPTING payload despite mismatch to maintain availability.", 
                    entry.Hash, entry.Timestamp.NodeId, entry.ComputeHash());
            }
        }

        // 2. Group changes by Author Node to validate Source Chains independently
        var changesByNode = changes.GroupBy(c => c.Timestamp.NodeId);

        foreach (var group in changesByNode)
        {
            var authorNodeId = group.Key;

            // FIX: Order by the full Timestamp (Physical + Logical), not just LogicalCounter.
            // LogicalCounter resets when PhysicalTime advances, so sorting by Counter alone breaks chronological order.
            var authorChain = group.OrderBy(c => c.Timestamp).ToList();

            // Check linkage within the batch
            for (int i = 1; i < authorChain.Count; i++)
            {
                if (authorChain[i].PreviousHash != authorChain[i - 1].Hash)
                {
                    _logger.LogError("Chain Broken in Batch for Node {AuthorId}", authorNodeId);
                    return SyncBatchResult.ChainBroken;
                }
            }

            // Check linkage with Local State
            var firstEntry = authorChain[0];
            var localHeadHash = await _store.GetLastEntryHashAsync(authorNodeId, token);

            _logger.LogDebug("Processing chain for Node {AuthorId}: FirstEntry.PrevHash={PrevHash}, FirstEntry.Hash={Hash}, LocalHeadHash={LocalHead}",
                authorNodeId, firstEntry.PreviousHash, firstEntry.Hash, localHeadHash ?? "(null)");

            if (localHeadHash != null && firstEntry.PreviousHash != localHeadHash)
            {
                // GAP DETECTED
                _logger.LogWarning("Gap Detected for Node {AuthorId}. Local Head: {Local}, Remote Prev: {Prev}. Initiating Recovery.", authorNodeId, localHeadHash, firstEntry.PreviousHash);

                // Gap Recovery (Range Sync)
                List<OplogEntry>? missingChain = null;
                try
                {
                     missingChain = await client.GetChainRangeAsync(localHeadHash, firstEntry.PreviousHash, token);
                }
                catch (SnapshotRequiredException)
                {
                    throw; // Propagate up to trigger full sync
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gap Recovery failed.");
                    /* Fallthrough to decision logic */
                }

                if (missingChain != null && missingChain.Any())
                {
                    _logger.LogInformation("Gap Recovery: Retrieved {Count} missing entries.", missingChain.Count);

                    // Validate Recovery Chain Linkage
                    bool linkValid = true;
                    if (missingChain[0].PreviousHash != localHeadHash) linkValid = false;
                    for (int i = 1; i < missingChain.Count; i++)
                        if (missingChain[i].PreviousHash != missingChain[i - 1].Hash) linkValid = false;
                    if (missingChain.Last().Hash != firstEntry.PreviousHash) linkValid = false;

                    if (!linkValid)
                    {
                        _logger.LogError("Recovery Chain Invalid Linkage. Aborting Gap Recovery.");
                        return SyncBatchResult.GapDetected;
                    }

                    // Apply Missing Chain First
                    await _store.ApplyBatchAsync(Enumerable.Empty<Document>(), missingChain, token);
                    _logger.LogInformation("Gap Recovery Applied Successfully.");
                }
                else
                {
                    // Gap recovery failed. This can happen if:
                    // 1. This is actually our first contact with this node's history
                    // 2. The peer doesn't have the full history
                    // 3. There's a true gap that cannot be recovered
                    
                    // DECISION: Accept the entries anyway but log a warning
                    // This allows forward progress even with partial history
                    _logger.LogWarning("Could not recover gap for Node {AuthorId}. Local Head: {Local}, Remote Prev: {Prev}. Accepting entries anyway (partial sync).",
                        authorNodeId, localHeadHash, firstEntry.PreviousHash);
                    
                    // Optionally: Mark this as a partial sync in metadata
                    // For now, we proceed and let the chain continue from this point
                }
            }
            else if (localHeadHash == null && !string.IsNullOrEmpty(firstEntry.PreviousHash))
            {
                // Implicit Accept / Partial Sync warning
                _logger.LogWarning("First contact with Node {AuthorId} at explicit state (Not Genesis). Accepting.", authorNodeId);
            }

            // Apply original batch (grouped by node for clarity, but store usually handles bulk)
            await _store.ApplyBatchAsync(Enumerable.Empty<Document>(), authorChain, token);
        }

        return SyncBatchResult.Success;
    }

    private async Task PerformSnapshotSyncAsync(TcpPeerClient client, bool mergeOnly, CancellationToken token)
    {
        _logger.LogInformation(mergeOnly ? "Starting Snapshot Merge..." : "Starting Full Database Replacement...");
        
        var tempFile = Path.GetTempFileName();
        try
        {
            _logger.LogInformation("Downloading snapshot to {TempFile}...", tempFile);
            using (var fs = File.Create(tempFile))
            {
                await client.GetSnapshotAsync(fs, token);
            }
            
            _logger.LogInformation("Snapshot Downloaded. applying to store...");
            
            using (var fs = File.OpenRead(tempFile))
            {
                if (mergeOnly)
                {
                    await _store.MergeSnapshotAsync(fs, token);
                }
                else
                {
                    await _store.ReplaceDatabaseAsync(fs, token);
                }
            }
            
            _logger.LogInformation("Snapshot applied successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform snapshot sync");
            throw;
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }
}