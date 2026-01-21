using EntglDb.Core;
using EntglDb.Core.Network; // For IMeshNetwork if we implement it
using EntglDb.Core.Storage;
using EntglDb.Network.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System;
using EntglDb.Network.HostedServices;
using EntglDb.Core.Sync;

namespace EntglDb.Network;

public static class EntglDbNetworkExtensions
{
    /// <summary>
    /// Adds EntglDb network services to the service collection.
    /// </summary>
    /// <param name="useHostedService">If true, registers EntglDbNodeService as IHostedService to automatically start/stop the node.</param>
    public static IServiceCollection AddEntglDbNetwork<TPeerNodeConfigurationProvider>(
        this IServiceCollection services,
        bool useHostedService = true,
        Action<ReconciliationOptions>? reconciliationOtions = null) 
        where TPeerNodeConfigurationProvider : class, IPeerNodeConfigurationProvider
    {
        services.TryAddSingleton<IPeerNodeConfigurationProvider, TPeerNodeConfigurationProvider>();

        services.TryAddSingleton<IAuthenticator, ClusterKeyAuthenticator>();
        
        services.TryAddSingleton<IPeerHandshakeService, SecureHandshakeService>();

        services.TryAddSingleton<IDiscoveryService, UdpDiscoveryService>();

        services.TryAddSingleton<IGapDetectionService, GapDetectionService>();

        services.TryAddSingleton<IReconciliationService, ReconciliationService>();

        services.TryAddSingleton<EntglDb.Network.Telemetry.INetworkTelemetryService>(sp => 
        {
            var logger = sp.GetRequiredService<ILogger<EntglDb.Network.Telemetry.NetworkTelemetryService>>();
            var path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "entgldb_metrics.bin");
            return new EntglDb.Network.Telemetry.NetworkTelemetryService(logger, path);
        });

        services.TryAddSingleton<ISyncServer, TcpSyncServer>();

        services.TryAddSingleton<ISyncOrchestrator, SyncOrchestrator>();

        services.TryAddSingleton<IEntglDbNode, EntglDbNode>();

        if (reconciliationOtions != null)
        {
            services.Configure(reconciliationOtions);
        }

        // Optionally register hosted service for automatic node lifecycle management
        if (useHostedService)
        {
            services.AddHostedService<EntglDbNodeService>();
            services.AddHostedService<ReconciliationBackgroundService>();
        }

        return services;
    }
}
