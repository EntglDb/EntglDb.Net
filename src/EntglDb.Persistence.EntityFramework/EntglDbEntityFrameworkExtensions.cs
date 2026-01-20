using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;

namespace EntglDb.Persistence.EntityFramework;

/// <summary>
/// Extension methods for configuring Entity Framework Core persistence for EntglDb.
/// </summary>
public static class EntglDbEntityFrameworkExtensions
{
    /// <summary>
    /// Adds Entity Framework Core persistence to EntglDb.
    /// You must configure the DbContext separately using AddDbContext.
    /// </summary>
    /// <example>
    /// services.AddEntglDbEntityFramework()
    ///     .AddDbContext&lt;EntglDbContext&gt;(options =>
    ///         options.UseSqlServer(connectionString));
    /// </example>
    public static IServiceCollection AddEntglDbEntityFramework(this IServiceCollection services)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));

        // Default Conflict Resolver (Last Write Wins) if none is provided
        services.TryAddSingleton<IConflictResolver, LastWriteWinsConflictResolver>();

        // Register EF Core Store
        services.TryAddScoped<IPeerStore, EfCorePeerStore>();

        return services;
    }

    /// <summary>
    /// Adds Entity Framework Core persistence to EntglDb with SQLite.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The SQLite connection string.</param>
    public static IServiceCollection AddEntglDbEntityFrameworkSqlite(
        this IServiceCollection services,
        string connectionString)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrEmpty(connectionString)) throw new ArgumentNullException(nameof(connectionString));

        services.AddDbContext<EntglDbContext>(options =>
            options.UseSqlite(connectionString));

        return services.AddEntglDbEntityFramework();
    }

    /// <summary>
    /// Adds Entity Framework Core persistence to EntglDb with SQL Server.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The SQL Server connection string.</param>
    public static IServiceCollection AddEntglDbEntityFrameworkSqlServer(
        this IServiceCollection services,
        string connectionString)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrEmpty(connectionString)) throw new ArgumentNullException(nameof(connectionString));

        services.AddDbContext<EntglDbContext>(options =>
            options.UseSqlServer(connectionString));

        return services.AddEntglDbEntityFramework();
    }

    /// <summary>
    /// Adds Entity Framework Core persistence to EntglDb with MySQL/MariaDB.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The MySQL connection string.</param>
    /// <param name="serverVersion">The MySQL server version.</param>
    public static IServiceCollection AddEntglDbEntityFrameworkMySql(
        this IServiceCollection services,
        string connectionString,
        ServerVersion? serverVersion = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (string.IsNullOrEmpty(connectionString)) throw new ArgumentNullException(nameof(connectionString));

        var version = serverVersion ?? ServerVersion.AutoDetect(connectionString);
        services.AddDbContext<EntglDbContext>(options =>
            options.UseMySql(connectionString, version));

        return services.AddEntglDbEntityFramework();
    }
}
