using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Core.Storage;

/// <summary>
/// Handles basic CRUD operations for documents.
/// </summary>
public interface IDocumentStore : ISnapshotable<Document>
{
    IEnumerable<string> InterestedCollection { get; }

    /// <summary>
    /// Asynchronously retrieves a incoming from the specified collection by its key.
    /// </summary>
    /// <param name="collection">The name of the collection containing the incoming to retrieve. Cannot be null or empty.</param>
    /// <param name="key">The unique key identifying the incoming within the collection. Cannot be null or empty.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the incoming if found; otherwise, null.</returns>
    Task<Document?> GetDocumentAsync(string collection, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves all documents belonging to the specified collection.
    /// </summary>
    /// <param name="collection">The name of the collection from which to retrieve documents. Cannot be null or empty.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains an enumerable collection of
    /// documents in the specified collection. The collection is empty if no documents are found.</returns>
    Task<IEnumerable<Document>> GetDocumentsByCollectionAsync(string collection, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously inserts a batch of documents into the data store.
    /// </summary>
    /// <param name="documents">The collection of documents to insert. Cannot be null or contain null elements.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if all documents
    /// were inserted successfully; otherwise, <see langword="false"/>.</returns>
    Task<bool> InsertBatchDocumentsAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously updates the specified incoming in the data store.
    /// </summary>
    /// <param name="document">The incoming to update. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the update operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if the incoming was
    /// successfully updated; otherwise, <see langword="false"/>.</returns>
    Task<bool> PutDocumentAsync(Document document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously updates a batch of documents in the data store.
    /// </summary>
    /// <param name="documents">The collection of documents to update. Cannot be null or contain null elements.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if all documents
    /// were updated successfully; otherwise, <see langword="false"/>.</returns>
    Task<bool> UpdateBatchDocumentsAsync(IEnumerable<Document> documents, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously deletes a incoming identified by the specified key from the given collection.
    /// </summary>
    /// <param name="collection">The name of the collection containing the incoming to delete. Cannot be null or empty.</param>
    /// <param name="key">The unique key identifying the incoming to delete. Cannot be null or empty.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the delete operation.</param>
    /// <returns>A task that represents the asynchronous delete operation. The task result is <see langword="true"/> if the
    /// incoming was successfully deleted; otherwise, <see langword="false"/>.</returns>
    Task<bool> DeleteDocumentAsync(string collection, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously deletes a batch of documents identified by their keys.
    /// </summary>
    /// <remarks>If any of the specified documents cannot be deleted, the method returns <see
    /// langword="false"/> but does not throw an exception. The operation is performed asynchronously and may complete
    /// partially if cancellation is requested.</remarks>
    /// <param name="documentKeys">A collection of incoming keys that specify the documents to delete. Cannot be null or contain null or empty
    /// values.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the delete operation.</param>
    /// <returns>A task that represents the asynchronous delete operation. The task result is <see langword="true"/> if all
    /// specified documents were successfully deleted; otherwise, <see langword="false"/>.</returns>
    Task<bool> DeleteBatchDocumentsAsync(IEnumerable<string> documentKeys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously merges the specified incoming with existing data and returns the updated incoming.
    /// </summary>
    /// <param name="incoming">The incoming to merge. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the merge operation.</param>
    /// <returns>A task that represents the asynchronous merge operation. The task result contains the merged incoming.</returns>
    Task<Document> MergeAsync(Document incoming, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves documents identified by the specified collection and key pairs.
    /// </summary>
    /// <param name="documentKeys">A list of tuples, each containing the collection name and the document key that uniquely identify the documents
    /// to retrieve. Cannot be null or empty.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous retrieval operation.</returns>
    Task<IEnumerable<Document>> GetDocumentsAsync(List<(string Collection, string Key)> documentKeys, CancellationToken cancellationToken);
}
