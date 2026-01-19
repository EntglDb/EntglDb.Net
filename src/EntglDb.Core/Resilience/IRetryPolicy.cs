using System;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Core.Resilience
{
    public interface IRetryPolicy
    {
        Task ExecuteAsync(Func<Task> operation, string operationName, CancellationToken cancellationToken = default);
        Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName, CancellationToken cancellationToken = default);
    }
}