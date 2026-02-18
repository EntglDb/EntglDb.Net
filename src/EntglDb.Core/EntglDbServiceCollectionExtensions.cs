using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using EntglDb.Core.Cache;
using EntglDb.Core.Diagnostics;
using EntglDb.Core.Resilience;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using System;

namespace EntglDb.Core
{
    public static class EntglDbServiceCollectionExtensions
    {
        public static IServiceCollection AddEntglDbCore(this IServiceCollection services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));

            // Infrastructure
            services.TryAddSingleton<IDocumentCache, DocumentCache>();
            services.TryAddSingleton<IOfflineQueue, OfflineQueue>();
            services.TryAddSingleton<ISyncStatusTracker, SyncStatusTracker>();
            services.TryAddSingleton<IRetryPolicy, RetryPolicy>();
            services.TryAddSingleton<IEntglDbHealthCheck, EntglDbHealthCheck>();

            return services;
        }
    }
}
