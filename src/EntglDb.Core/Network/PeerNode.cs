using System;

namespace EntglDb.Core.Network
{
    public class PeerNode
    {
        public string NodeId { get; }
        public string Address { get; } // IP:Port
        public DateTimeOffset LastSeen { get; }

        public PeerNode(string nodeId, string address, DateTimeOffset lastSeen)
        {
            NodeId = nodeId;
            Address = address;
            LastSeen = lastSeen;
        }
    }
}
