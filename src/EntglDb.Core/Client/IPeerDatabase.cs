using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Core
{
    /// <summary>
    /// Represents the main database interface for EntglDb.
    /// </summary>
    public interface IPeerDatabase
    {
        /// <summary>
        /// Gets a collection by name.
        /// </summary>
        IPeerCollection Collection(string name);
        
        /// <summary>
        /// Gets a strongly-typed collection. The collection name defaults to the type name in lowercase.
        /// </summary>
        /// <typeparam name="T">The entity type for this collection.</typeparam>
        /// <param name="customName">Optional custom collection name. If null, uses typeof(T).Name.ToLowerInvariant().</param>
        IPeerCollection<T> Collection<T>(string? customName = null);
        
        /// <summary>
        /// Manually triggers synchronization.
        /// </summary>
        Task SyncAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves a list of all active collections in the database.
        /// </summary>
        Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Represents a non-generic collection of documents.
    /// </summary>
    public interface IPeerCollection
    {
        string Name { get; }

        Task Put(string key, object document, CancellationToken cancellationToken = default);
        Task PutMany(IEnumerable<KeyValuePair<string, object>> documents, CancellationToken cancellationToken = default);
        Task<T> Get<T>(string key, CancellationToken cancellationToken = default);
        Task Delete(string key, CancellationToken cancellationToken = default);
        Task DeleteMany(IEnumerable<string> keys, CancellationToken cancellationToken = default);
        Task<int> Count(Expression<Func<object, bool>>? predicate = null, CancellationToken cancellationToken = default); // Weakly typed predicate? Or just rely on generic?
        // IPeerCollection is weakly typed usually. But Expression<Func<object, bool>> is hard to use.
        // Let's omit Count from non-generic IPeerCollection for now unless requested.
        // Wait, user asked for "Count with filter".
        // Actually IPeerCollection<T> is where the filter makes sense.
        // Let's add it to generic only for now, or use QueryNode if we want non-generic?
        // Let's stick to IPeerCollection<T> as per typical pattern.
        
        Task<IEnumerable<T>> Find<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
        Task<IEnumerable<T>> Find<T>(Expression<Func<T, bool>> predicate, int? skip, int? take, string? orderBy = null, bool ascending = true, CancellationToken cancellationToken = default);
    }

    public class ChangesAppliedEventArgs : EventArgs
    {
        public IEnumerable<OplogEntry> Changes { get; }
        public ChangesAppliedEventArgs(IEnumerable<OplogEntry> changes)
        {
            Changes = changes;
        }
    }

    /// <summary>
    /// Represents a strongly-typed collection of documents.
    /// </summary>
    /// <typeparam name="T">The entity type for this collection.</typeparam>
    public interface IPeerCollection<T> : IPeerCollection
    {
        /// <summary>
        /// Inserts or updates a document with an explicit key.
        /// </summary>
        Task Put(string key, T document, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Inserts or updates a document. The key is extracted from the entity's primary key property.
        /// If the key is empty and auto-generation is enabled, a new GUID will be generated.
        /// </summary>
        Task Put(T document, CancellationToken cancellationToken = default);

        /// <summary>
        /// Inserts or updates multiple documents.
        /// </summary>
        Task PutMany(IEnumerable<T> documents, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Retrieves a document by key.
        /// </summary>
        Task<T> Get(string key, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Queries documents matching the predicate.
        /// </summary>
        Task<IEnumerable<T>> Find(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Queries documents with paging and sorting.
        /// </summary>
        Task<IEnumerable<T>> Find(Expression<Func<T, bool>> predicate, int? skip, int? take, string? orderBy = null, bool ascending = true, CancellationToken cancellationToken = default);

        /// <summary>
        /// Counts documents matching the predicate.
        /// </summary>
        Task<int> Count(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default);
    }
}
