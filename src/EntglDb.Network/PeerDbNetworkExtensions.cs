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

        services.TryAddSingleton<IPeerConnectionPool>(sp =>
        {
            var configProvider = sp.GetRequiredService<IPeerNodeConfigurationProvider>();
            var handshakeService = sp.GetService<IPeerHandshakeService>();
            var telemetry = sp.GetService<EntglDb.Network.Telemetry.INetworkTelemetryService>();
            var logger = sp.GetRequiredService<ILogger<TcpPeerClient>>();
            return new PeerConnectionPool(
                addr => new TcpPeerClient(addr, logger, handshakeService, telemetry),
                configProvider,
                logger);
        });

        services.TryAddSingleton<IPeerMessenger, PeerMessenger>();

        return services;
    }

    /// <summary>
    /// Registers the node facade for a consumer that uses EntglDb purely as a peer-to-peer transport and
    /// replicates no document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call <c>AddEntglDbNetwork&lt;TConfig&gt;()</c> before this. Do not call it alongside
    /// <c>AddEntglDbSync()</c>: that registers <see cref="INetworkNode"/> itself, bound to the same instance as
    /// <c>IEntglDbNode</c>, so a node with sync enabled is not started twice.
    /// </para>
    /// <para>
    /// Without <c>AddEntglDbSync()</c> nothing handles the sync message types (0-15). That is the point -
    /// this node answers only the <see cref="INetworkMessageHandler"/> implementations its consumer
    /// registers - but it does mean a peer that tries to sync against it is refused.
    /// </para>
    /// </remarks>
    /// <param name="useHostedService">
    /// If <c>true</c> (default), registers <see cref="NetworkNodeService"/> so the node starts and stops with
    /// the application.
    /// </param>
    public static IServiceCollection AddEntglDbNetworkNode(
        this IServiceCollection services,
        bool useHostedService = true)
    {
        services.TryAddSingleton<INetworkNode, NetworkNode>();

        if (useHostedService)
        {
            services.AddHostedService<NetworkNodeService>();
        }

        return services;
    }
}
