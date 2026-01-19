using EntglDb.Core.Network;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Core.Sync;

/// <summary>
/// Queue for operations performed while offline.
/// </summary>
public class OfflineQueue : IOfflineQueue
{
    private readonly IPeerNodeConfigurationProvider _peerNodeConfigurationProvider;
    private readonly Queue<PendingOperation> _queue = new();
    private readonly ILogger<OfflineQueue> _logger;
    private readonly object _lock = new();

    public OfflineQueue(IPeerNodeConfigurationProvider peerNodeConfigurationProvider, ILogger<OfflineQueue>? logger = null)
    {
        _peerNodeConfigurationProvider = peerNodeConfigurationProvider;
        _logger = logger ?? NullLogger<OfflineQueue>.Instance;
    }

    /// <summary>
    /// Gets the number of pending operations.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _queue.Count;
            }
        }
    }

    /// <summary>
    /// Enqueues an operation for later execution.
    /// </summary>
    public async Task Enqueue(PendingOperation operation)
    {
        var config = await _peerNodeConfigurationProvider.GetConfiguration();
        lock (_lock)
        {
            if (_queue.Count >= config.MaxQueueSize)
            {
                var dropped = _queue.Dequeue();
                _logger.LogWarning("Queue full, dropped oldest operation: {Type} {Collection}:{Key}",
                    dropped.Type, dropped.Collection, dropped.Key);
            }

            _queue.Enqueue(operation);
            _logger.LogDebug("Queued {Type} operation for {Collection}:{Key}",
                operation.Type, operation.Collection, operation.Key);
        }
    }

    /// <summary>
    /// Flushes all pending operations.
    /// </summary>
    public async Task<(int Successful, int Failed)> FlushAsync(Func<PendingOperation, Task> executor, CancellationToken cancellationToken = default)
    {
        List<PendingOperation> operations;

        lock (_lock)
        {
            operations = _queue.ToList();
            _queue.Clear();
        }

        if (operations.Count == 0)
        {
            _logger.LogDebug("No pending operations to flush");
            return (0, 0);
        }

        _logger.LogInformation("Flushing {Count} pending operations", operations.Count);

        int successful = 0;
        int failed = 0;

        foreach (var op in operations)
        {
            try
            {
                await executor(op);
                successful++;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "Failed to execute pending {Type} operation for {Collection}:{Key}",
                    op.Type, op.Collection, op.Key);
            }
        }

        _logger.LogInformation("Flush completed: {Successful} successful, {Failed} failed",
            successful, failed);

        return (successful, failed);
    }

    /// <summary>
    /// Clears all pending operations.
    /// </summary>
    public async Task Clear()
    {
        lock (_lock)
        {
            var count = _queue.Count;
            _queue.Clear();
            _logger.LogInformation("Cleared {Count} pending operations", count);
        }
    }
}
