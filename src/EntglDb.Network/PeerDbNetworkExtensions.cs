using EntglDb.Core.Network;
using EntglDb.Network.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System;

namespace EntglDb.Network;

public static class EntglDbNetworkExtensions
{
    /// <summary>
    /// Adds EntglDb transport-layer network services to the service collection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registers transport-layer services: <see cref="IPeerNodeConfigurationProvider"/>,
    /// <see cref="IAuthenticator"/>, <see cref="IPeerHandshakeService"/>, <see cref="IDiscoveryService"/>,
    /// telemetry, and <see cref="ISyncServer"/>.
    /// </para>
    /// <para>
    /// To register sync handlers and the node orchestrator, also call <c>AddEntglDbSync()</c>
    /// from the <c>EntglDb.Sync</c> package.
    /// </para>
    /// <para>
    /// To add custom handlers, register your own <see cref="INetworkMessageHandler"/>
    /// implementations after calling this method.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddEntglDbNetwork<TPeerNodeConfigurationProvider>(
        this IServiceCollection services)
        where TPeerNodeConfigurationProvider : class, IPeerNodeConfigurationProvider
    {
        services.TryAddSingleton<IPeerNodeConfigurationProvider, TPeerNodeConfigurationProvider>();

        services.TryAddSingleton<IAuthenticator, ClusterKeyAuthenticator>();
        
        services.TryAddSingleton<IPeerHandshakeService, SecureHandshakeService>();

        services.TryAddSingleton<IDiscoveryService, UdpDiscoveryService>();

        services.TryAddSingleton<EntglDb.Network.Telemetry.INetworkTelemetryService>(sp => 
        {
            var logger = sp.GetRequiredService<ILogger<EntglDb.Network.Telemetry.NetworkTelemetryService>>();
            var path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "entgldb_metrics.bin");
            return new EntglDb.Network.Telemetry.NetworkTelemetryService(logger, path);
        });

        services.TryAddSingleton<ISyncServer, TcpSyncServer>();

        return services;
    }
}
