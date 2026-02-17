using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Core.Storage;

/// <summary>
/// Defines the contract for storing and retrieving document metadata for sync tracking.
/// Document metadata stores HLC timestamps and deleted state without modifying application entities.
/// </summary>
public interface IDocumentMetadataStore : ISnapshotable<DocumentMetadata>
{
    /// <summary>
    /// Gets the metadata for a specific document.
    /// </summary>
    /// <param name="collection">The collection name.</param>
    /// <param name="key">The document key.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The document metadata if found; otherwise null.</returns>
    Task<DocumentMetadata?> GetMetadataAsync(string collection, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets metadata for all documents in a collection.
    /// </summary>
    /// <param name="collection">The collection name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Enumerable of document metadata for the collection.</returns>
    Task<IEnumerable<DocumentMetadata>> GetMetadataByCollectionAsync(string collection, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts (inserts or updates) metadata for a document.
    /// </summary>
    /// <param name="metadata">The metadata to upsert.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task UpsertMetadataAsync(DocumentMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts metadata for multiple documents in batch.
    /// </summary>
    /// <param name="metadatas">The metadata items to upsert.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task UpsertMetadataBatchAsync(IEnumerable<DocumentMetadata> metadatas, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a document as deleted by setting IsDeleted=true and updating the timestamp.
    /// </summary>
    /// <param name="collection">The collection name.</param>
    /// <param name="key">The document key.</param>
    /// <param name="timestamp">The HLC timestamp of the deletion.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task MarkDeletedAsync(string collection, string key, HlcTimestamp timestamp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all document metadata with timestamps after the specified timestamp.
    /// Used for incremental sync to find documents modified since last sync.
    /// </summary>
    /// <param name="since">The timestamp to compare against.</param>
    /// <param name="collections">Optional collection filter.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Documents modified after the specified timestamp.</returns>
    Task<IEnumerable<DocumentMetadata>> GetMetadataAfterAsync(HlcTimestamp since, IEnumerable<string>? collections = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents metadata for a document used in sync tracking.
/// </summary>
public class DocumentMetadata
{
    /// <summary>
    /// Gets or sets the collection name.
    /// </summary>
    public string Collection { get; set; } = "";

    /// <summary>
    /// Gets or sets the document key.
    /// </summary>
    public string Key { get; set; } = "";

    /// <summary>
    /// Gets or sets the HLC timestamp of the last modification.
    /// </summary>
    public HlcTimestamp UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets whether this document is marked as deleted (tombstone).
    /// </summary>
    public bool IsDeleted { get; set; }

    public DocumentMetadata() { }

    public DocumentMetadata(string collection, string key, HlcTimestamp updatedAt, bool isDeleted = false)
    {
        Collection = collection;
        Key = key;
        UpdatedAt = updatedAt;
        IsDeleted = isDeleted;
    }
}
