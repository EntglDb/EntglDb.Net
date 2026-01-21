using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Persistence.EntityFramework;

namespace EntglDb.Persistence.PostgreSQL;

/// <summary>
/// Extension methods for configuring PostgreSQL persistence for EntglDb.
/// </summary>
public static class PostgreSqlExtensions
{
    /// <summary>
    /// Adds PostgreSQL persistence to EntglDb with JSONB support.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="configureOptions">Optional action to configure additional DbContext options.</param>
    public static IServiceCollection AddEntglDbPostgreSql(
        this IServiceCollection services,
        string connectionString,
        Action<DbContextOptionsBuilder>? configureOptions = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrEmpty(connectionString)) throw new ArgumentNullException(nameof(connectionString));

        // Register PostgreSQL DbContext
        services.AddDbContext<EntglDbContext, PostgreSqlDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
            });
            
            configureOptions?.Invoke(options);
        });

        // Default Conflict Resolver (Last Write Wins) if none is provided
        services.TryAddSingleton<IConflictResolver, LastWriteWinsConflictResolver>();

        // Register PostgreSQL Store
        services.TryAddScoped<IPeerStore, PostgreSqlPeerStore>();

        return services;
    }
}
