using System;
using System.Collections.Generic;

namespace EntglDb.Core.Diagnostics
{
    /// <summary>
    /// Represents the health status of an EntglDb instance.
    /// </summary>
    public class HealthStatus
    {
        /// <summary>
        /// Indicates if the database is healthy.
        /// </summary>
        public bool DatabaseHealthy { get; set; }

        /// <summary>
        /// Indicates if network connectivity is available.
        /// </summary>
        public bool NetworkHealthy { get; set; }

        /// <summary>
        /// Number of currently connected peers.
        /// </summary>
        public int ConnectedPeers { get; set; }

        /// <summary>
        /// Timestamp of the last successful sync operation.
        /// </summary>
        public DateTime? LastSyncTime { get; set; }

        /// <summary>
        /// List of recent errors.
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// Overall health status.
        /// </summary>
        public bool IsHealthy => DatabaseHealthy && NetworkHealthy && Errors.Count == 0;

        /// <summary>
        /// Additional diagnostic information.
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Represents the synchronization status.
    /// </summary>
    public class SyncStatus
    {
        /// <summary>
        /// Indicates if the node is currently online.
        /// </summary>
        public bool IsOnline { get; set; }

        /// <summary>
        /// Timestamp of the last sync operation.
        /// </summary>
        public DateTime? LastSyncTime { get; set; }

        /// <summary>
        /// Number of pending operations in the offline queue.
        /// </summary>
        public int PendingOperations { get; set; }

        /// <summary>
        /// List of active peer nodes.
        /// </summary>
        public List<PeerInfo> ActivePeers { get; set; } = new();

        /// <summary>
        /// Recent sync errors.
        /// </summary>
        public List<SyncError> SyncErrors { get; set; } = new();

        /// <summary>
        /// Total number of documents synced.
        /// </summary>
        public long TotalDocumentsSynced { get; set; }

        /// <summary>
        /// Total bytes transferred.
        /// </summary>
        public long TotalBytesTransferred { get; set; }
    }

    /// <summary>
    /// Information about a peer node.
    /// </summary>
    public class PeerInfo
    {
        /// <summary>
        /// Unique identifier of the peer.
        /// </summary>
        public string NodeId { get; set; } = "";

        /// <summary>
        /// Network address of the peer.
        /// </summary>
        public string Address { get; set; } = "";

        /// <summary>
        /// Last time the peer was seen.
        /// </summary>
        public DateTime LastSeen { get; set; }

        /// <summary>
        /// Indicates if the peer is currently connected.
        /// </summary>
        public bool IsConnected { get; set; }

        /// <summary>
        /// Number of successful syncs with this peer.
        /// </summary>
        public int SuccessfulSyncs { get; set; }

        /// <summary>
        /// Number of failed syncs with this peer.
        /// </summary>
        public int FailedSyncs { get; set; }
    }

    /// <summary>
    /// Represents a synchronization error.
    /// </summary>
    public class SyncError
    {
        /// <summary>
        /// Timestamp when the error occurred.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Error message.
        /// </summary>
        public string Message { get; set; } = "";

        /// <summary>
        /// Peer node ID if applicable.
        /// </summary>
        public string? PeerNodeId { get; set; }

        /// <summary>
        /// Error code.
        /// </summary>
        public string? ErrorCode { get; set; }
    }
}
