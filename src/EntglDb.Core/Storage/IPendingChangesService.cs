using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core;

namespace EntglDb.Core.Storage;

/// <summary>
/// Service for recording and retrieving pending local changes before they are flushed to the oplog.
/// Implementations use upsert semantics: one entry per (collection, key) pair; last write wins.
/// </summary>
public interface IPendingChangesService
{
    /// <summary>
    /// Records a local change (Put or Delete) synchronously.
    /// If a pending change already exists for (collection, key), it is overwritten (upsert semantics).
    /// Must be thread-safe and non-blocking (typically in-memory ConcurrentDictionary).
    /// </summary>
    /// <param name="collection">The collection name.</param>
    /// <param name="key">The document key.</param>
    /// <param name="operationType">The operation type (Put or Delete).</param>
    /// <param name="timestamp">The HLC timestamp when the change was detected.</param>
    void RecordChange(string collection, string key, OperationType operationType, HlcTimestamp timestamp);

    /// <summary>
    /// Atomically retrieves all pending changes and clears the buffer.
    /// Called by the flush service at each sync cycle start.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A read-only list of all pending changes (snapshot); cleared after return.</returns>
    Task<IReadOnlyList<PendingChange>> PopAllAsync(CancellationToken cancellationToken = default);
}
