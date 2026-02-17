using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Persistence.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EntglDb.Persistence.BLite;

/// <summary>
/// Extension methods for configuring BLite persistence for EntglDb.
/// </summary>
public static class EntglDbBLiteExtensions
{
    /// <summary>
    /// Adds BLite persistence to EntglDb using a custom DbContext and DocumentStore implementation.
    /// </summary>
    /// <typeparam name="TDbContext">The type of the BLite document database context. Must inherit from EntglDocumentDbContext.</typeparam>
    /// <typeparam name="TDocumentStore">The type of the document store implementation. Must implement IDocumentStore.</typeparam>
    /// <param name="services">The service collection to add the services to.</param>
    /// <param name="contextFactory">A factory function that creates the DbContext instance.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddEntglDbBLite<TDbContext, TDocumentStore>(
        this IServiceCollection services,
        Func<IServiceProvider, TDbContext> contextFactory) 
        where TDbContext : EntglDocumentDbContext
        where TDocumentStore : class, IDocumentStore
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (contextFactory == null) throw new ArgumentNullException(nameof(contextFactory));

        // Register the DbContext as singleton (must match store lifetime)
        services.TryAddSingleton<TDbContext>(contextFactory);
        services.TryAddSingleton<EntglDocumentDbContext>(sp => sp.GetRequiredService<TDbContext>());

        // Default Conflict Resolver (Last Write Wins) if none is provided
        services.TryAddSingleton<IConflictResolver, LastWriteWinsConflictResolver>();

        // Register BLite Stores (all Singleton)
        services.TryAddSingleton<IOplogStore, BLiteOplogStore<TDbContext>>();
        services.TryAddSingleton<IPeerConfigurationStore, BLitePeerConfigurationStore<TDbContext>>();
        services.TryAddSingleton<ISnapshotMetadataStore, BLiteSnapshotMetadataStore<TDbContext>>();
        services.TryAddSingleton<IDocumentMetadataStore, BLiteDocumentMetadataStore<TDbContext>>();
        
        // Register the DocumentStore implementation
        services.TryAddSingleton<IDocumentStore, TDocumentStore>();
        
        // Register the SnapshotService (uses the generic SnapshotStore from EntglDb.Persistence)
        services.TryAddSingleton<ISnapshotService, SnapshotStore>();

        return services;
    }

    /// <summary>
    /// Adds BLite persistence to EntglDb using a custom DbContext (without explicit DocumentStore type).
    /// </summary>
    /// <typeparam name="TDbContext">The type of the BLite document database context. Must inherit from EntglDocumentDbContext.</typeparam>
    /// <param name="services">The service collection to add the services to.</param>
    /// <param name="contextFactory">A factory function that creates the DbContext instance.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>You must manually register IDocumentStore after calling this method.</remarks>
    public static IServiceCollection AddEntglDbBLite<TDbContext>(
        this IServiceCollection services,
        Func<IServiceProvider, TDbContext> contextFactory) 
        where TDbContext : EntglDocumentDbContext
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (contextFactory == null) throw new ArgumentNullException(nameof(contextFactory));

        // Register the DbContext as singleton
        services.TryAddSingleton<TDbContext>(contextFactory);
        services.TryAddSingleton<EntglDocumentDbContext>(sp => sp.GetRequiredService<TDbContext>());

        // Default Conflict Resolver (Last Write Wins) if none is provided
        services.TryAddSingleton<IConflictResolver, LastWriteWinsConflictResolver>();

        // Register BLite Stores (all Singleton)
        services.TryAddSingleton<IOplogStore, BLiteOplogStore<TDbContext>>();
        services.TryAddSingleton<IPeerConfigurationStore, BLitePeerConfigurationStore<TDbContext>>();
        services.TryAddSingleton<ISnapshotMetadataStore, BLiteSnapshotMetadataStore<TDbContext>>();
        services.TryAddSingleton<IDocumentMetadataStore, BLiteDocumentMetadataStore<TDbContext>>();
        
        // Register the SnapshotService (uses the generic SnapshotStore from EntglDb.Persistence)
        services.TryAddSingleton<ISnapshotService, SnapshotStore>();

        return services;
    }
}

/// <summary>
/// Options for configuring BLite persistence.
/// </summary>
public class BLiteOptions
{
    /// <summary>
    /// Gets or sets the file path to the BLite database file.
    /// </summary>
    public string DatabasePath { get; set; } = "";
}
