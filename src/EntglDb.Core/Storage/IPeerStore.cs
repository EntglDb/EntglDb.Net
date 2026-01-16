using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core; // Added for ChangesAppliedEventArgs

namespace EntglDb.Core.Storage
{
    public interface IPeerStore
    {
        // Document Operations
        /// <summary>
        /// Occurs when changes are applied to the store from external sources (sync).
        /// </summary>
        event EventHandler<ChangesAppliedEventArgs> ChangesApplied;

        Task SaveDocumentAsync(Document document, CancellationToken cancellationToken = default);
        Task<Document?> GetDocumentAsync(string collection, string key, CancellationToken cancellationToken = default);

        // Oplog Operations
        Task AppendOplogEntryAsync(OplogEntry entry, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Retrieves oplog entries strictly greater than the given timestamp.
        /// </summary>
        Task<IEnumerable<OplogEntry>> GetOplogAfterAsync(HlcTimestamp timestamp, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Retrieves the latest HLC timestamp known by the store (max of log or docs).
        /// </summary>
        Task<HlcTimestamp> GetLatestTimestampAsync(CancellationToken cancellationToken = default);

        // Atomic Batch (for Sync)
        Task ApplyBatchAsync(IEnumerable<Document> documents, IEnumerable<OplogEntry> oplogEntries, CancellationToken cancellationToken = default);

        // Query

        /// <summary>
        /// Queries documents in a collection.
        /// </summary>
        /// <summary>
        /// Queries documents in a collection.
        /// </summary>
        Task<IEnumerable<Document>> QueryDocumentsAsync(string collection, QueryNode? queryExpression, int? skip = null, int? take = null, string? orderBy = null, bool ascending = true, CancellationToken cancellationToken = default);

        /// <summary>
        /// Counts documents in a collection matching the query.
        /// </summary>
        Task<int> CountDocumentsAsync(string collection, QueryNode? queryExpression, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves a list of all active collections in the store.
        /// </summary>
        Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Ensures an index exists on a specific JSON property within a collection.
        /// </summary>
        Task EnsureIndexAsync(string collection, string propertyPath, CancellationToken cancellationToken = default);
    }
}
