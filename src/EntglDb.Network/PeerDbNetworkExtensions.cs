using EntglDb.Core;
using EntglDb.Core.Network; // For IMeshNetwork if we implement it
using EntglDb.Core.Storage;
using EntglDb.Network.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using System;

namespace EntglDb.Network;

public static class EntglDbNetworkExtensions
{
    /// <summary>
    /// Adds EntglDb network services to the service collection.
    /// </summary>
    /// <param name="useHostedService">If true, registers EntglDbNodeService as IHostedService to automatically start/stop the node.</param>
    public static IServiceCollection AddEntglDbNetwork<TPeerNodeConfigurationProvider>(
        this IServiceCollection services,
        bool useHostedService = true) 
        where TPeerNodeConfigurationProvider : class, IPeerNodeConfigurationProvider
    {
        services.TryAddSingleton<IPeerNodeConfigurationProvider, TPeerNodeConfigurationProvider>();

        services.TryAddSingleton<IAuthenticator, ClusterKeyAuthenticator>();
        
        services.TryAddSingleton<IPeerHandshakeService, SecureHandshakeService>();

        services.TryAddSingleton<IDiscoveryService, UdpDiscoveryService>();

        services.TryAddSingleton<ISyncServer, TcpSyncServer>();

        services.TryAddSingleton<ISyncOrchestrator, SyncOrchestrator>();

        services.TryAddSingleton<IEntglDbNode, EntglDbNode>();

        // Optionally register hosted service for automatic node lifecycle management
        if (useHostedService)
        {
            services.AddHostedService<EntglDbNodeService>();
        }

        return services;
    }
}
