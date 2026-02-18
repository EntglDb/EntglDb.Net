using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Core.Storage;

public interface ISnapshotable<T>
{
    /// <summary>
    /// Asynchronously deletes the underlying data store and all of its contents.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the drop operation.</param>
    /// <remarks>After calling this method, the data store and all stored data will be permanently removed.
    /// This operation cannot be undone. Any further operations on the data store may result in errors.</remarks>
    /// <returns>A task that represents the asynchronous drop operation.</returns>
    Task DropAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously exports a collection of items of type T.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the export operation.</param>
    /// <returns>A task that represents the asynchronous export operation. The task result contains an enumerable collection of
    /// exported items of type T.</returns>
    Task<IEnumerable<T>> ExportAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports the specified collection of items asynchronously.
    /// </summary>
    /// <param name="items">The collection of items to import. Cannot be null. Each item will be processed in sequence.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the import operation.</param>
    /// <returns>A task that represents the asynchronous import operation.</returns>
    Task ImportAsync(IEnumerable<T> items, CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges the specified collection of items into the target data store asynchronously.
    /// </summary>
    /// <remarks>If the operation is canceled via the provided cancellation token, the returned task will be
    /// in a canceled state. The merge operation may update existing items or add new items, depending on the
    /// implementation.</remarks>
    /// <param name="items">The collection of items to merge into the data store. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the merge operation.</param>
    /// <returns>A task that represents the asynchronous merge operation.</returns>
    Task MergeAsync(IEnumerable<T> items, CancellationToken cancellationToken = default);
}
