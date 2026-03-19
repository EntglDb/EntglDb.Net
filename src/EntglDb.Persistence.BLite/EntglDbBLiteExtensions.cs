using BLite.Core;
using EntglDb.Core;
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
    /// <typeparam name="TDbContext">The type of the BLite document database context (user application context).</typeparam>
    /// <typeparam name="TDocumentStore">The type of the document store implementation. Must implement IDocumentStore.</typeparam>
    /// <param name="services">The service collection to add the services to.</param>
    /// <param name="contextFactory">A factory function that creates the DbContext instance.</param>
/// <param name="metaDatabasePath">Path to the internal metadata database (required).</param>
/// <returns>The service collection for chaining.</returns>
public static IServiceCollection AddEntglDbBLite<TDbContext, TDocumentStore>(
    this IServiceCollection services,
    Func<IServiceProvider, TDbContext> contextFactory,
    string metaDatabasePath) 
    where TDbContext : DocumentDbContext
    where TDocumentStore : class, IDocumentStore
{
    if (services == null) throw new ArgumentNullException(nameof(services));
    if (contextFactory == null) throw new ArgumentNullException(nameof(contextFactory));
    if (string.IsNullOrWhiteSpace(metaDatabasePath)) throw new ArgumentException("metaDatabasePath is required", nameof(metaDatabasePath));

    // Register the user DbContext as singleton
    services.TryAddSingleton<TDbContext>(contextFactory);

    // Register internal metadata context (EntglDbMetaContext)
    services.TryAddSingleton<EntglDbMetaContext>(_ => new EntglDbMetaContext(metaDatabasePath));

    // Register PendingChangesService
    services.TryAddSingleton<IPendingChangesService, BLitePendingChangesService>();

    // Register PendingChangesFlushService (flushes pending changes to oplog before each sync cycle)
    services.TryAddSingleton<IPendingChangesFlushService, PendingChangesFlushService>();

    // Default Conflict Resolver (Last Write Wins) if none is provided
    services.TryAddSingleton<IConflictResolver, LastWriteWinsConflictResolver>();

    // Vector Clock Service (shared between DocumentStore and OplogStore)
    services.TryAddSingleton<IVectorClockService, VectorClockService>();

    // Register the DocumentStore implementation first
    services.TryAddSingleton<IDocumentStore, TDocumentStore>();
    
    // Register BLite Stores using the metadata context
    services.TryAddSingleton<IOplogStore>(sp =>
        new BLiteOplogStore(
            sp.GetRequiredService<EntglDbMetaContext>(),
            sp.GetRequiredService<IDocumentStore>(),
            sp.GetRequiredService<IConflictResolver>(),
            sp.GetRequiredService<IVectorClockService>(),
            sp.GetRequiredService<ISnapshotMetadataStore>()));
    
    services.TryAddSingleton<IPeerConfigurationStore>(sp =>
        new BLitePeerConfigurationStore(
            sp.GetRequiredService<EntglDbMetaContext>()));
    
    services.TryAddSingleton<ISnapshotMetadataStore>(sp =>
        new BLiteSnapshotMetadataStore(
            sp.GetRequiredService<EntglDbMetaContext>()));
    
    services.TryAddSingleton<IDocumentMetadataStore>(sp =>
        new BLiteDocumentMetadataStore(
            sp.GetRequiredService<EntglDbMetaContext>()));

    // Register the SnapshotService (uses the generic SnapshotStore from EntglDb.Persistence)
    services.TryAddSingleton<ISnapshotService, SnapshotStore>();

    return services;
}

    /// <summary>
    /// Adds BLite persistence to EntglDb using a custom DbContext (without explicit DocumentStore type).
    /// </summary>
    /// <typeparam name="TDbContext">The type of the BLite document database context (user application context).</typeparam>
    /// <param name="services">The service collection to add the services to.</param>
    /// <param name="contextFactory">A factory function that creates the DbContext instance.</param>
    /// <param name="metaDatabasePath">Path to the internal metadata database (required).</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>You must manually register IDocumentStore after calling this method.</remarks>
    public static IServiceCollection AddEntglDbBLite<TDbContext>(
        this IServiceCollection services,
        Func<IServiceProvider, TDbContext> contextFactory,
        string metaDatabasePath) 
        where TDbContext : DocumentDbContext
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (contextFactory == null) throw new ArgumentNullException(nameof(contextFactory));
        if (string.IsNullOrWhiteSpace(metaDatabasePath)) throw new ArgumentException("metaDatabasePath is required", nameof(metaDatabasePath));

        // Register the user DbContext as singleton
        services.TryAddSingleton<TDbContext>(contextFactory);

        // Register internal metadata context (EntglDbMetaContext)
        services.TryAddSingleton<EntglDbMetaContext>(_ => new EntglDbMetaContext(metaDatabasePath));

        // Register PendingChangesService
        services.TryAddSingleton<IPendingChangesService, BLitePendingChangesService>();

        // Register PendingChangesFlushService (flushes pending changes to oplog before each sync cycle)
        services.TryAddSingleton<IPendingChangesFlushService, PendingChangesFlushService>();

        // Default Conflict Resolver (Last Write Wins) if none is provided
        services.TryAddSingleton<IConflictResolver, LastWriteWinsConflictResolver>();

        // Vector Clock Service (shared between DocumentStore and OplogStore)
        services.TryAddSingleton<IVectorClockService, VectorClockService>();

        // Register BLite Stores using the metadata context
        services.TryAddSingleton<IOplogStore>(sp =>
            new BLiteOplogStore(
                sp.GetRequiredService<EntglDbMetaContext>(),
                sp.GetRequiredService<IDocumentStore>(),
                sp.GetRequiredService<IConflictResolver>(),
                sp.GetRequiredService<IVectorClockService>(),
                sp.GetRequiredService<ISnapshotMetadataStore>()));
        
        services.TryAddSingleton<IPeerConfigurationStore>(sp =>
            new BLitePeerConfigurationStore(sp.GetRequiredService<EntglDbMetaContext>()));
        
        services.TryAddSingleton<ISnapshotMetadataStore>(sp =>
            new BLiteSnapshotMetadataStore(sp.GetRequiredService<EntglDbMetaContext>()));
        
        services.TryAddSingleton<IDocumentMetadataStore>(sp =>
            new BLiteDocumentMetadataStore(sp.GetRequiredService<EntglDbMetaContext>()));
        
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
