using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core; // Added for ChangesAppliedEventArgs
using EntglDb.Core.Network; // Added for RemotePeerConfiguration

namespace EntglDb.Core.Storage;

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

    /// <summary>
    /// Retrieves a vector clock representing the latest known timestamp for each node.
    /// </summary>
    Task<VectorClock> GetVectorClockAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves oplog entries for a specific node after a given timestamp.
    /// </summary>
    Task<IEnumerable<OplogEntry>> GetOplogForNodeAfterAsync(string nodeId, HlcTimestamp since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the hash of the last oplog entry for a specific node.
    /// Used to build the chain when appending new entries or validating sync.
    /// </summary>
    Task<string?> GetLastEntryHashAsync(string nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a range of oplog entries connecting two hashes (exclusive of start, inclusive of end).
    /// Used for Gap Recovery.
    /// </summary>
    Task<IEnumerable<OplogEntry>> GetChainRangeAsync(string startHash, string endHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a specific oplog entry by its hash.
    /// </summary>
    Task<OplogEntry?> GetEntryByHashAsync(string hash, CancellationToken cancellationToken = default);

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

    // Remote Peer Management
    /// <summary>
    /// Saves or updates a remote peer configuration in the persistent store.
    /// </summary>
    /// <param name="peer">The remote peer configuration to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveRemotePeerAsync(RemotePeerConfiguration peer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all remote peer configurations from the persistent store.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of remote peer configurations.</returns>
    Task<IEnumerable<RemotePeerConfiguration>> GetRemotePeersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a remote peer configuration from the persistent store.
    /// </summary>
    /// <param name="nodeId">The unique identifier of the peer to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveRemotePeerAsync(string nodeId, CancellationToken cancellationToken = default);
}
