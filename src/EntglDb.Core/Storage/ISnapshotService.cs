using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Core.Storage;

/// <summary>
/// Handles full database lifecycle operations such as snapshots, replacement, and clearing data.
/// </summary>
public interface ISnapshotService
{
    /// <summary>
    /// Asynchronously creates a snapshot of the current state and writes it to the specified destination stream.
    /// </summary>
    /// <param name="destination">The stream to which the snapshot data will be written.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the snapshot creation operation.</param>
    /// <returns>A task that represents the asynchronous snapshot creation operation.</returns>
    Task CreateSnapshotAsync(Stream destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the existing database with the contents provided in the specified stream asynchronously.
    /// </summary>
    /// <param name="databaseStream">A stream containing the new database data to be used for replacement.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous database replacement operation.</returns>
    Task ReplaceDatabaseAsync(Stream databaseStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges the provided snapshot stream into the current data store asynchronously.
    /// </summary>
    /// <param name="snapshotStream">A stream containing the snapshot data to be merged.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the merge operation.</param>
    /// <returns>A task that represents the asynchronous merge operation.</returns>
    Task MergeSnapshotAsync(Stream snapshotStream, CancellationToken cancellationToken = default);
}
