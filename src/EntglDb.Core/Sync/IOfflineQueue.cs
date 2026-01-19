using System;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Core.Sync
{
    public interface IOfflineQueue
    {
        int Count { get; }

        Task Clear();
        Task Enqueue(PendingOperation operation);
        Task<(int Successful, int Failed)> FlushAsync(Func<PendingOperation, Task> executor, CancellationToken cancellationToken = default);
    }
}