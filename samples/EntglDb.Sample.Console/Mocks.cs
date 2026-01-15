using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core;

using EntglDb.Core.Storage;

namespace EntglDb.Sample.Console.Mocks
{
    public class MockStore : IPeerStore
    {
        private readonly ConcurrentDictionary<string, Document> _docs = new ConcurrentDictionary<string, Document>();
        private readonly List<OplogEntry> _oplog = new List<OplogEntry>();
        private readonly object _logLock = new object();

        public Task SaveDocumentAsync(Document document, CancellationToken cancellationToken = default)
        {
            _docs[document.Key] = document; 
            return Task.CompletedTask;
        }

        public Task<Document?> GetDocumentAsync(string collection, string key, CancellationToken cancellationToken = default)
        {
            _docs.TryGetValue(key, out var doc);
            return Task.FromResult(doc);
        }

        public Task AppendOplogEntryAsync(OplogEntry entry, CancellationToken cancellationToken = default)
        {
            lock (_logLock)
            {
                _oplog.Add(entry);
            }
            return Task.CompletedTask;
        }

        public Task<IEnumerable<OplogEntry>> GetOplogAfterAsync(HlcTimestamp timestamp, CancellationToken cancellationToken = default)
        {
            lock (_logLock)
            {
                var res = _oplog.Where(e => e.Timestamp.CompareTo(timestamp) > 0).ToList();
                return Task.FromResult((IEnumerable<OplogEntry>)res);
            }
        }

        public Task<HlcTimestamp> GetLatestTimestampAsync(CancellationToken cancellationToken = default)
        {
            lock (_logLock)
            {
                var last = _oplog.LastOrDefault();
                return Task.FromResult(last?.Timestamp ?? new HlcTimestamp(0, 0, "node1"));
            }
        }

        public Task ApplyBatchAsync(IEnumerable<Document> documents, IEnumerable<OplogEntry> oplogEntries, CancellationToken cancellationToken = default)
        {
            foreach (var doc in documents)
            {
                _docs[doc.Key] = doc;
            }
            lock (_logLock)
            {
                _oplog.AddRange(oplogEntries);
            }
            return Task.CompletedTask;
        }

        public Task<IEnumerable<Document>> QueryDocumentsAsync(string collection, QueryNode queryExpression, int? skip = null, int? take = null, string? orderBy = null, bool ascending = true, CancellationToken cancellationToken = default)
        {
             // Mock query: return all and let memory filter
             // We'll return all docs for now to demo Find.
             return Task.FromResult((IEnumerable<Document>)_docs.Values.ToList()); 
        }
    }


}
