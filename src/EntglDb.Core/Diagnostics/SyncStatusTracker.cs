using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Core.Diagnostics;

/// <summary>
/// Tracks synchronization status and provides diagnostics.
/// </summary>
public class SyncStatusTracker : ISyncStatusTracker
{
    private readonly ILogger<SyncStatusTracker> _logger;
    private readonly object _lock = new();

    private bool _isOnline = false;
    private DateTime? _lastSyncTime;
    private readonly List<PeerInfo> _activePeers = new();
    private readonly Queue<SyncError> _recentErrors = new();
    private long _totalDocumentsSynced = 0;
    private long _totalBytesTransferred = 0;
    private const int MaxErrorHistory = 50;

    public SyncStatusTracker(ILogger<SyncStatusTracker>? logger = null)
    {
        _logger = logger ?? NullLogger<SyncStatusTracker>.Instance;
    }

    /// <summary>
    /// Updates online status.
    /// </summary>
    public void SetOnlineStatus(bool isOnline)
    {
        lock (_lock)
        {
            if (_isOnline != isOnline)
            {
                _isOnline = isOnline;
                _logger.LogInformation("Status changed to {Status}", isOnline ? "Online" : "Offline");
            }
        }
    }

    /// <summary>
    /// Records a successful sync operation.
    /// </summary>
    public void RecordSync(int documentCount, long bytesTransferred)
    {
        lock (_lock)
        {
            _lastSyncTime = DateTime.UtcNow;
            _totalDocumentsSynced += documentCount;
            _totalBytesTransferred += bytesTransferred;

            _logger.LogDebug("Synced {Count} documents ({Bytes} bytes)", documentCount, bytesTransferred);
        }
    }

    /// <summary>
    /// Records a sync error.
    /// </summary>
    public void RecordError(string message, string? peerNodeId = null, string? errorCode = null)
    {
        lock (_lock)
        {
            var error = new SyncError
            {
                Timestamp = DateTime.UtcNow,
                Message = message,
                PeerNodeId = peerNodeId,
                ErrorCode = errorCode
            };

            _recentErrors.Enqueue(error);

            while (_recentErrors.Count > MaxErrorHistory)
            {
                _recentErrors.Dequeue();
            }

            _logger.LogWarning("Sync error recorded: {Message} (Peer: {Peer})", message, peerNodeId ?? "N/A");
        }
    }

    /// <summary>
    /// Updates peer information.
    /// </summary>
    public void UpdatePeer(string nodeId, string address, bool isConnected)
    {
        lock (_lock)
        {
            var peer = _activePeers.FirstOrDefault(p => p.NodeId == nodeId);

            if (peer == null)
            {
                peer = new PeerInfo
                {
                    NodeId = nodeId,
                    Address = address,
                    IsConnected = isConnected,
                    LastSeen = DateTime.UtcNow
                };
                _activePeers.Add(peer);
                _logger.LogInformation("New peer discovered: {NodeId} at {Address}", nodeId, address);
            }
            else
            {
                peer.Address = address;
                peer.IsConnected = isConnected;
                peer.LastSeen = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// Records successful sync with a peer.
    /// </summary>
    public void RecordPeerSuccess(string nodeId)
    {
        lock (_lock)
        {
            var peer = _activePeers.FirstOrDefault(p => p.NodeId == nodeId);
            if (peer != null)
            {
                peer.SuccessfulSyncs++;
            }
        }
    }

    /// <summary>
    /// Records failed sync with a peer.
    /// </summary>
    public void RecordPeerFailure(string nodeId)
    {
        lock (_lock)
        {
            var peer = _activePeers.FirstOrDefault(p => p.NodeId == nodeId);
            if (peer != null)
            {
                peer.FailedSyncs++;
            }
        }
    }

    /// <summary>
    /// Gets current sync status.
    /// </summary>
    public SyncStatus GetStatus()
    {
        lock (_lock)
        {
            return new SyncStatus
            {
                IsOnline = _isOnline,
                LastSyncTime = _lastSyncTime,
                PendingOperations = 0, // Will be set by caller if offline queue is available
                ActivePeers = _activePeers.ToList(),
                SyncErrors = _recentErrors.ToList(),
                TotalDocumentsSynced = _totalDocumentsSynced,
                TotalBytesTransferred = _totalBytesTransferred
            };
        }
    }

    /// <summary>
    /// Cleans up inactive peers.
    /// </summary>
    public void CleanupInactivePeers(TimeSpan inactiveThreshold)
    {
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow - inactiveThreshold;
            var removed = _activePeers.RemoveAll(p => p.LastSeen < cutoff);

            if (removed > 0)
            {
                _logger.LogInformation("Removed {Count} inactive peers", removed);
            }
        }
    }
}
