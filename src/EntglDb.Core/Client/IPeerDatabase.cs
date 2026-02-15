using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Core;

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

    // Direct CRUD operations
    Task PutAsync(string collection, string key, object document, CancellationToken cancellationToken = default);
    Task PutManyAsync(string collection, IEnumerable<KeyValuePair<string, object>> documents, CancellationToken cancellationToken = default);
    Task<T?> GetAsync<T>(string collection, string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string collection, string key, CancellationToken cancellationToken = default);
    Task DeleteManyAsync(string collection, IEnumerable<string> keys, CancellationToken cancellationToken = default);
    Task<IEnumerable<T>> FindAsync<T>(string collection, Expression<Func<T, bool>> predicate, int? skip = null, int? take = null, string? orderBy = null, bool ascending = true, CancellationToken cancellationToken = default);
    Task<int> CountAsync<T>(string collection, Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Manually triggers synchronization.
    /// </summary>
    Task SyncAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a list of all active collections in the database.
    /// </summary>
    Task<IEnumerable<string>> GetCollectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously performs any necessary initialization for the component.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the initialization operation.</param>
    /// <returns>A task that represents the asynchronous initialization operation.</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
