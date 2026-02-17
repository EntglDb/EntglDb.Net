using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Core.Storage;

/// <summary>
/// Handles operations related to the Operation Log (Oplog), synchronization, and logical clocks.
/// </summary>
public interface IOplogStore : ISnapshotable<OplogEntry>
{
    /// <summary>
    /// Occurs when changes are applied to the store from external sources (sync).
    /// </summary>
    event EventHandler<ChangesAppliedEventArgs> ChangesApplied;

    /// <summary>
    /// Appends a new entry to the operation log asynchronously.
    /// </summary>
    /// <param name="entry">The operation log entry to append. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the append operation.</param>
    /// <returns>A task that represents the asynchronous append operation.</returns>
    Task AppendOplogEntryAsync(OplogEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves all oplog entries that occurred after the specified timestamp.
    /// </summary>
    /// <param name="timestamp">The timestamp after which oplog entries should be returned.</param>
    /// <param name="collections">An optional collection of collection names to filter the results.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation containing matching oplog entries.</returns>
    Task<IEnumerable<OplogEntry>> GetOplogAfterAsync(HlcTimestamp timestamp, IEnumerable<string>? collections = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves the latest observed hybrid logical clock (HLC) timestamp.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation containing the latest HLC timestamp.</returns>
    Task<HlcTimestamp> GetLatestTimestampAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves the current vector clock representing the state of distributed events.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation containing the current vector clock.</returns>
    Task<VectorClock> GetVectorClockAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a collection of oplog entries for the specified node that occurred after the given timestamp.
    /// </summary>
    /// <param name="nodeId">The unique identifier of the node for which to retrieve oplog entries. Cannot be null or empty.</param>
    /// <param name="since">The timestamp after which oplog entries should be returned.</param>
    /// <param name="collections">An optional collection of collection names to filter the oplog entries.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation containing oplog entries for the specified node.</returns>
    Task<IEnumerable<OplogEntry>> GetOplogForNodeAfterAsync(string nodeId, HlcTimestamp since, IEnumerable<string>? collections = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves the hash of the most recent entry for the specified node.
    /// </summary>
    /// <param name="nodeId">The unique identifier of the node for which to retrieve the last entry hash. Cannot be null or empty.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation containing the hash string of the last entry or null.</returns>
    Task<string?> GetLastEntryHashAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves a sequence of oplog entries representing the chain between the specified start and end hashes.
    /// </summary>
    /// <param name="startHash">The hash of the first entry in the chain range. Cannot be null or empty.</param>
    /// <param name="endHash">The hash of the last entry in the chain range. Cannot be null or empty.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation containing OplogEntry objects in chain order.</returns>
    Task<IEnumerable<OplogEntry>> GetChainRangeAsync(string startHash, string endHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves the oplog entry associated with the specified hash value.
    /// </summary>
    /// <param name="hash">The hash string identifying the oplog entry to retrieve. Cannot be null or empty.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous operation containing the OplogEntry if found, otherwise null.</returns>
    Task<OplogEntry?> GetEntryByHashAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a batch of oplog entries asynchronously to the target data store.
    /// </summary>
    /// <param name="oplogEntries">A collection of OplogEntry objects representing the operations to apply. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the batch operation.</param>
    /// <returns>A task that represents the asynchronous batch apply operation.</returns>
    Task ApplyBatchAsync(IEnumerable<OplogEntry> oplogEntries, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously removes entries from the oplog that are older than the specified cutoff timestamp.
    /// </summary>
    /// <param name="cutoff">The timestamp that defines the upper bound for entries to be pruned.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the prune operation.</param>
    /// <returns>A task that represents the asynchronous prune operation.</returns>
    Task PruneOplogAsync(HlcTimestamp cutoff, CancellationToken cancellationToken = default);

}
