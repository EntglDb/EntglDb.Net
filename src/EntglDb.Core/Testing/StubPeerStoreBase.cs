using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core;

namespace EntglDb.Core.Testing
{
    /// <summary>
    /// Base class for stub/mock implementations of IPeerStore in tests.
    /// Provides default implementations for gap detection methods.
    /// </summary>
    public abstract class StubPeerStoreBase : Storage.IPeerStore
    {
        public virtual event EventHandler<ChangesAppliedEventArgs>? ChangesApplied;

        public abstract Task SaveDocumentAsync(Document document, CancellationToken cancellationToken = default);
        public abstract Task<Document?> GetDocumentAsync(string collection, string key, CancellationToken cancellationToken = default);
        public abstract Task AppendOplogEntryAsync(OplogEntry entry, CancellationToken cancellationToken = default);
        public abstract Task<IEnumerable<OplogEntry>> GetOplogAfterAsync(HlcTimestamp timestamp, CancellationToken cancellationToken = default);
        public abstract Task<HlcTimestamp> GetLatestTimestampAsync(CancellationToken cancellationToken = default);
        public abstract Task ApplyBatchAsync(IEnumerable<Document> documents, IEnumerable<OplogEntry> oplogEntries, CancellationToken cancellationToken = default);
        public abstract Task<IEnumerable<Document>> QueryDocumentsAsync(string collection, QueryNode? queryExpression, int? skip = null, int? take = null, string? orderBy = null, bool ascending = true, CancellationToken cancellationToken = default);
        public abstract Task<int> CountDocumentsAsync(string collection, QueryNode? queryExpression, CancellationToken cancellationToken = default);
        public abstract Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default);
        public abstract Task EnsureIndexAsync(string collection, string propertyPath, CancellationToken cancellationToken = default);
        public abstract Task SaveRemotePeerAsync(Network.RemotePeerConfiguration peer, CancellationToken cancellationToken = default);
        public abstract Task<IEnumerable<Network.RemotePeerConfiguration>> GetRemotePeersAsync(CancellationToken cancellationToken = default);
        public abstract Task RemoveRemotePeerAsync(string nodeId, CancellationToken cancellationToken = default);

        // Default stub implementations for gap detection (can be overridden if needed)
        public virtual Task<long> GetCurrentSequenceNumberAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0L);
        }

        public virtual Task<Dictionary<string, long>> GetPeerSequenceNumbersAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new Dictionary<string, long>());
        }

        public virtual Task<IEnumerable<OplogEntry>> GetOplogBySequenceNumbersAsync(string nodeId, IEnumerable<long> sequenceNumbers, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Enumerable.Empty<OplogEntry>());
        }
    }
}
