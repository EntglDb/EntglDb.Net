using EntglDb.Core;
using EntglDb.Core.Network; // For IMeshNetwork if we implement it
using EntglDb.Core.Storage;
using EntglDb.Network.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System;

namespace EntglDb.Network;

public static class EntglDbNetworkExtensions
{
    public static IServiceCollection AddEntglDbNetwork<TPeerNodeConfigurationProvider>(this IServiceCollection services) 
        where TPeerNodeConfigurationProvider : class, IPeerNodeConfigurationProvider
    {
        services.TryAddSingleton<IPeerNodeConfigurationProvider, TPeerNodeConfigurationProvider>();

        services.TryAddSingleton<IAuthenticator, ClusterKeyAuthenticator>();
        
        services.TryAddSingleton<IPeerHandshakeService, SecureHandshakeService>();

        services.TryAddSingleton<IDiscoveryService, UdpDiscoveryService>();

        services.TryAddSingleton<ISyncServer, TcpSyncServer>();

        services.TryAddSingleton<ISyncOrchestrator, SyncOrchestrator>();

        services.TryAddSingleton<IEntglDbNode, EntglDbNode>();

        return services;
    }
}
