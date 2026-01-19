using System;

namespace EntglDb.Core.Diagnostics
{
    public interface ISyncStatusTracker
    {
        void CleanupInactivePeers(TimeSpan inactiveThreshold);
        SyncStatus GetStatus();
        void RecordError(string message, string? peerNodeId = null, string? errorCode = null);
        void RecordPeerFailure(string nodeId);
        void RecordPeerSuccess(string nodeId);
        void RecordSync(int documentCount, long bytesTransferred);
        void SetOnlineStatus(bool isOnline);
        void UpdatePeer(string nodeId, string address, bool isConnected);
    }
}