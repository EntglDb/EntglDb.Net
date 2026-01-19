using System.Threading.Tasks;

namespace EntglDb.Network
{
    public interface ISyncOrchestrator
    {
        Task Start();
        Task Stop();
    }
}