using System.Threading.Tasks;

namespace EntglDb.Network
{
    /// <summary>
    /// A running peer on the mesh: the TCP server that answers requests and the discovery service that
    /// finds the others.
    /// </summary>
    /// <remarks>
    /// This is the whole node for a consumer that uses EntglDb as a peer-to-peer transport - discovery,
    /// <see cref="IPeerMessenger"/> and its own <see cref="INetworkMessageHandler"/> implementations - and
    /// replicates no document. <see cref="IEntglDbNode"/> in EntglDb.Sync extends it with the sync
    /// orchestrator, for a consumer that does.
    /// </remarks>
    public interface INetworkNode
    {
        /// <summary>The address other peers can reach this node at.</summary>
        NodeAddress Address { get; }

        /// <summary>LAN peer discovery.</summary>
        IDiscoveryService Discovery { get; }

        /// <summary>The server answering incoming requests.</summary>
        ISyncServer Server { get; }

        Task Start();

        Task Stop();
    }
}
