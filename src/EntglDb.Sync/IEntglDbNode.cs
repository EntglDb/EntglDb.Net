using System.Threading.Tasks;

namespace EntglDb.Network
{
    /// <summary>
    /// A peer node that also replicates documents: <see cref="INetworkNode"/> plus the sync orchestrator.
    /// </summary>
    public interface IEntglDbNode : INetworkNode
    {
        ISyncOrchestrator Orchestrator { get; }
    }
}
