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
    }

    /// <summary>
    /// Represents a non-generic collection of documents.
    /// </summary>
    public interface IPeerCollection
    {
        string Name { get; }

        Task Put(string key, object document, CancellationToken cancellationToken = default);
        Task<T> Get<T>(string key, CancellationToken cancellationToken = default);
        Task Delete(string key, CancellationToken cancellationToken = default);
        
        Task<IEnumerable<T>> Find<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
        Task<IEnumerable<T>> Find<T>(Expression<Func<T, bool>> predicate, int? skip, int? take, string? orderBy = null, bool ascending = true, CancellationToken cancellationToken = default);
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
    }
}
