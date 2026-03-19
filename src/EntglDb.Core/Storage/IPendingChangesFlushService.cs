using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Core.Storage;

/// <summary>
/// Flushes all pending local changes (recorded by IPendingChangesService) into the oplog.
/// Called by SyncOrchestrator at the start of each sync cycle, before sending/receiving deltas.
/// </summary>
public interface IPendingChangesFlushService
{
    /// <summary>
    /// Atomically pops all pending changes and writes an oplog entry for each one.
    /// Idempotent: duplicate content (same ContentHash) is skipped.
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
