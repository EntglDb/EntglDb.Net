using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Core.Storage;

/// <summary>
/// Manages Vector Clock state for the local node.
/// Tracks the latest timestamp and hash per node for sync coordination.
/// </summary>
public interface IVectorClockService
{
    /// <summary>
    /// Indicates whether the cache has been populated with initial data.
    /// Reset to false by <see cref="Invalidate"/>.
    /// </summary>
    bool IsInitialized { get; set; }

    /// <summary>
    /// Updates the cache with a new OplogEntry's timestamp and hash.
    /// Called by both DocumentStore (local CDC) and OplogStore (remote sync).
    /// </summary>
    void Update(OplogEntry entry);

    /// <summary>
    /// Returns the current Vector Clock built from cached node timestamps.
    /// </summary>
    Task<VectorClock> GetVectorClockAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the latest known timestamp across all nodes.
    /// </summary>
    Task<HlcTimestamp> GetLatestTimestampAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the last known hash for the specified node.
    /// Returns null if the node is unknown.
    /// </summary>
    string? GetLastHash(string nodeId);

    /// <summary>
    /// Updates the cache with a specific node's timestamp and hash.
    /// Used for snapshot metadata fallback.
    /// </summary>
    void UpdateNode(string nodeId, HlcTimestamp timestamp, string hash);

    /// <summary>
    /// Clears the cache and resets <see cref="IsInitialized"/> to false,
    /// forcing re-initialization on next access.
    /// </summary>
    void Invalidate();
}
