using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Core.Storage;

public interface ISnapshotMetadataStore : ISnapshotable<SnapshotMetadata>
{
    /// <summary>
    /// Asynchronously retrieves the snapshot metadata associated with the specified node identifier.
    /// </summary>
    /// <param name="nodeId">The unique identifier of the node for which to retrieve snapshot metadata. Cannot be null or empty.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the <see cref="SnapshotMetadata"/>
    /// for the specified node if found; otherwise, <see langword="null"/>.</returns>
    Task<SnapshotMetadata?> GetSnapshotMetadataAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously inserts the specified snapshot metadata into the data store.
    /// </summary>
    /// <param name="metadata">The snapshot metadata to insert. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous insert operation.</returns>
    Task InsertSnapshotMetadataAsync(SnapshotMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously updates the metadata for an existing snapshot.
    /// </summary>
    /// <param name="existingMeta">The metadata object representing the snapshot to update. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous update operation.</returns>
    Task UpdateSnapshotMetadataAsync(SnapshotMetadata existingMeta, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves the hash of the current snapshot for the specified node.
    /// </summary>
    /// <param name="nodeId">The unique identifier of the node for which to obtain the snapshot hash.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task containing the snapshot hash as a string, or null if no snapshot is available.</returns>
    Task<string?> GetSnapshotHashAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all snapshot metadata entries. Used for initializing VectorClock cache.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>All snapshot metadata entries.</returns>
    Task<IEnumerable<SnapshotMetadata>> GetAllSnapshotMetadataAsync(CancellationToken cancellationToken = default);
}
