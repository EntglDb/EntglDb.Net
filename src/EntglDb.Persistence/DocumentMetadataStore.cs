using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core;
using EntglDb.Core.Storage;

namespace EntglDb.Persistence.Sqlite;

/// <summary>
/// Abstract base class for document metadata storage implementations.
/// Provides common functionality for tracking document HLC timestamps for sync.
/// </summary>
public abstract class DocumentMetadataStore : IDocumentMetadataStore
{
    /// <inheritdoc />
    public abstract Task<DocumentMetadata?> GetMetadataAsync(string collection, string key, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<IEnumerable<DocumentMetadata>> GetMetadataByCollectionAsync(string collection, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task UpsertMetadataAsync(DocumentMetadata metadata, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task UpsertMetadataBatchAsync(IEnumerable<DocumentMetadata> metadatas, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task MarkDeletedAsync(string collection, string key, HlcTimestamp timestamp, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<IEnumerable<DocumentMetadata>> GetMetadataAfterAsync(HlcTimestamp since, IEnumerable<string>? collections = null, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task DropAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<IEnumerable<DocumentMetadata>> ExportAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task ImportAsync(IEnumerable<DocumentMetadata> items, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task MergeAsync(IEnumerable<DocumentMetadata> items, CancellationToken cancellationToken = default);
}
