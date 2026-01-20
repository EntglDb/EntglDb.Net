using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using EntglDb.Core.Network;
using System;
using System.IO;
using System.Threading.Tasks;

namespace EntglDb.Persistence.Sqlite
{
    public static class EntglDbSqliteExtensions
    {
        /// <summary>
        /// Adds SQLite persistence to EntglDb with configurable options.
        /// Database path is dynamically constructed based on NodeId from IPeerNodeConfigurationProvider.
        /// </summary>
        public static IServiceCollection AddEntglDbSqlite(
            this IServiceCollection services, 
            Action<SqlitePersistenceOptions>? configureOptions = null)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            // Configure options
            var options = new SqlitePersistenceOptions();
            configureOptions?.Invoke(options);
            services.TryAddSingleton(options);

            // Default Conflict Resolver (Recursive Node Merge) if none is provided
            services.TryAddSingleton<IConflictResolver, RecursiveNodeMergeConflictResolver>();

            // Register Sqlite Store with factory that resolves configuration provider
            services.TryAddSingleton<IPeerStore>(sp => 
            {
                var logger = sp.GetRequiredService<ILogger<SqlitePeerStore>>();
                var resolver = sp.GetRequiredService<IConflictResolver>();
                var configProvider = sp.GetRequiredService<IPeerNodeConfigurationProvider>();
                var persistOptions = sp.GetRequiredService<SqlitePersistenceOptions>();

                return new SqlitePeerStore(configProvider, persistOptions, logger, resolver);
            });

            return services;
        }

        /// <summary>
        /// Adds SQLite persistence to EntglDb using a direct connection string (legacy support).
        /// This overload is provided for backward compatibility.
        /// </summary>
        public static IServiceCollection AddEntglDbSqlite(
            this IServiceCollection services, 
            string connectionString)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (string.IsNullOrEmpty(connectionString)) throw new ArgumentNullException(nameof(connectionString));

            // For legacy mode, we don't use dynamic paths
            // Create a special options instance that signals direct connection string usage
            var options = new SqlitePersistenceOptions
            {
                BasePath = null, // Signal to use connection string directly
                UsePerCollectionTables = false // Legacy single-table mode
            };
            
            services.TryAddSingleton(options);

            // Default Conflict Resolver (Last Write Wins) if none is provided
            services.TryAddSingleton<IConflictResolver, LastWriteWinsConflictResolver>();

            // Register Sqlite Store with direct connection string
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
