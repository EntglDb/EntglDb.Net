using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using EntglDb.Core.Storage;

namespace EntglDb.Core.Diagnostics
{
    /// <summary>
    /// Tracks synchronization status and provides diagnostics.
    /// </summary>
    public class SyncStatusTracker
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

    /// <summary>
    /// Provides health check functionality.
    /// </summary>
    public class EntglDbHealthCheck
    {
        private readonly IPeerStore _store;
        private readonly SyncStatusTracker _syncTracker;
        private readonly ILogger<EntglDbHealthCheck> _logger;

        public EntglDbHealthCheck(
            IPeerStore store, 
            SyncStatusTracker syncTracker,
            ILogger<EntglDbHealthCheck>? logger = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _syncTracker = syncTracker ?? throw new ArgumentNullException(nameof(syncTracker));
            _logger = logger ?? NullLogger<EntglDbHealthCheck>.Instance;
        }

        /// <summary>
        /// Performs a comprehensive health check.
        /// </summary>
        public async Task<HealthStatus> CheckAsync(CancellationToken cancellationToken = default)
        {
            var status = new HealthStatus();
            
            // Check database health
            try
            {
                // Try to get latest timestamp (simple database operation)
                var timestamp = await _store.GetLatestTimestampAsync(cancellationToken);
                status.DatabaseHealthy = true;
                _logger.LogDebug("Database health check passed (latest timestamp: {Timestamp})", timestamp);
            }
            catch (Exception ex)
            {
                status.DatabaseHealthy = false;
                status.Errors.Add($"Database check failed: {ex.Message}");
                _logger.LogError(ex, "Database health check failed");
            }
            
            // Get sync status
            var syncStatus = _syncTracker.GetStatus();
            status.NetworkHealthy = syncStatus.IsOnline;
            status.ConnectedPeers = syncStatus.ActivePeers.Count(p => p.IsConnected);
            status.LastSyncTime = syncStatus.LastSyncTime;
            
            // Add error messages from sync tracker
            foreach (var error in syncStatus.SyncErrors.Take(5)) // Last 5 errors
            {
                status.Errors.Add($"{error.Timestamp:yyyy-MM-dd HH:mm:ss} - {error.Message}");
            }
            
            // Add metadata
            status.Metadata["TotalDocumentsSynced"] = syncStatus.TotalDocumentsSynced;
            status.Metadata["TotalBytesTransferred"] = syncStatus.TotalBytesTransferred;
            status.Metadata["ActivePeers"] = syncStatus.ActivePeers.Count;
            
            _logger.LogInformation("Health check completed: Database={DbHealth}, Network={NetHealth}, Peers={Peers}", 
                status.DatabaseHealthy, status.NetworkHealthy, status.ConnectedPeers);
            
            return status;
        }
    }
}
