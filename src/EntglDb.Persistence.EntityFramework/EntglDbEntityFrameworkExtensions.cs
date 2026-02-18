using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Persistence.EntityFramework.Entities;

namespace EntglDb.Persistence.EntityFramework;

/// <summary>
/// Extension methods for configuring Entity Framework Core persistence for EntglDb.
/// </summary>
public static class EntglDbEntityFrameworkExtensions
{
    /// <summary>
    /// Adds Entity Framework Core persistence to EntglDb.
    /// You must configure the DbContext separately using AddDbContext with your chosen database provider.
    /// </summary>
    public static IServiceCollection AddEntglDbEntityFramework<TDbContext>(this IServiceCollection services) where TDbContext : DbContext
    {
        if (services == null) throw new ArgumentNullException(nameof(services));

        // Default Conflict Resolver (Last Write Wins) if none is provided
        services.TryAddSingleton<IConflictResolver, LastWriteWinsConflictResolver>();

        // Register EF Core Store
        services.TryAddSingleton<IOplogStore, EfCoreOplogStore<TDbContext>>();
        services.TryAddSingleton<IPeerConfigurationStore, EfCorePeerConfigurationStore<TDbContext>>();
        services.TryAddSingleton<ISnapshotMetadataStore, EfCoreSnapshotMetadaStore<TDbContext>>();

        return services;
    }

    public static ModelBuilder ApplyEntglDbEntityFrameworkConfigurations(this ModelBuilder modelBuilder)
    {
        if (modelBuilder == null) throw new ArgumentNullException(nameof(modelBuilder));
        modelBuilder.ApplyConfiguration(new OplogEntityConfiguration());
        modelBuilder.ApplyConfiguration(new RemotePeerEntityConfiguration());
        modelBuilder.ApplyConfiguration(new SnapshotMetadataEntityConfiguration());
        modelBuilder.ApplyConfiguration(new DocumentMetadataEntityConfiguration());
        return modelBuilder;
    }
}
