using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Core.Diagnostics
{
    public interface IEntglDbHealthCheck
    {
        Task<HealthStatus> CheckAsync(CancellationToken cancellationToken = default);
    }
}