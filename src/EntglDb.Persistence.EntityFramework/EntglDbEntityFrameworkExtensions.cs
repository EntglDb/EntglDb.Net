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
    /// You must configure the DbContext separately using AddDbContext with your chosen database provider.
    /// </summary>
    /// <example>
    /// // SQL Server example
    /// services.AddDbContext&lt;EntglDbContext&gt;(options =>
    ///     options.UseSqlServer(connectionString));
    /// services.AddEntglDbEntityFramework();
    /// 
    /// // PostgreSQL example
    /// services.AddDbContext&lt;EntglDbContext&gt;(options =>
    ///     options.UseNpgsql(connectionString));
    /// services.AddEntglDbEntityFramework();
    /// 
    /// // SQLite example
    /// services.AddDbContext&lt;EntglDbContext&gt;(options =>
    ///     options.UseSqlite(connectionString));
    /// services.AddEntglDbEntityFramework();
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
}
