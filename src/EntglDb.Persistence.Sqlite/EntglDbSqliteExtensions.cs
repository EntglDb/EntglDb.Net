using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using System;

namespace EntglDb.Persistence.Sqlite
{
    public static class EntglDbSqliteExtensions
    {
        public static IServiceCollection AddEntglDbSqlite(this IServiceCollection services, string connectionString)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (string.IsNullOrEmpty(connectionString)) throw new ArgumentNullException(nameof(connectionString));

            // Default Conflict Resolver (Last Write Wins) if none is provided
            services.TryAddSingleton<IConflictResolver, LastWriteWinsConflictResolver>();

            // Register Sqlite Store
            services.TryAddSingleton<IPeerStore>(sp => 
            {
                var logger = sp.GetRequiredService<ILogger<SqlitePeerStore>>();
                var resolver = sp.GetRequiredService<IConflictResolver>();
                return new SqlitePeerStore(connectionString, logger, resolver);
            });

            return services;
        }
    }
}
