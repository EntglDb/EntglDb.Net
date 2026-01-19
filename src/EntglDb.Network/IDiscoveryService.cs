using EntglDb.Core.Network;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EntglDb.Network
{
    public interface IDiscoveryService
    {
        IEnumerable<PeerNode> GetActivePeers();
        Task Start();
        Task Stop();
    }
}