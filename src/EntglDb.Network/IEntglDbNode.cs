using System.Threading.Tasks;

namespace EntglDb.Network
{
    public interface IEntglDbNode
    {
        NodeAddress Address { get; }
        IDiscoveryService Discovery { get; }
        ISyncOrchestrator Orchestrator { get; }
        ISyncServer Server { get; }

        Task Start();
        Task Stop();
    }
}