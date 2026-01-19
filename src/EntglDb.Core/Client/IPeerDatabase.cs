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
