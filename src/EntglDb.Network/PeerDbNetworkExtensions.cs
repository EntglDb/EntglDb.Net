using EntglDb.Core;
using EntglDb.Core.Network; // For IMeshNetwork if we implement it
using EntglDb.Core.Storage;
using EntglDb.Network.Handlers;
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
    /// <remarks>
    /// <para>
    /// Six core <see cref="INetworkMessageHandler"/> implementations are registered automatically
    /// (<c>GetClockHandler</c>, <c>GetVectorClockHandler</c>, <c>PullChangesHandler</c>,
    /// <c>PushChangesHandler</c>, <c>GetChainRangeHandler</c>, <c>GetSnapshotHandler</c>).
    /// </para>
    /// <para>
    /// To add custom handlers or override a core handler, register your own
    /// <see cref="INetworkMessageHandler"/> implementations <em>after</em> calling this method:
    /// </para>
    /// <code>
    /// services.AddEntglDbNetwork&lt;MyConfigProvider&gt;();
    /// services.AddSingleton&lt;INetworkMessageHandler, MyCustomHandler&gt;();
    /// </code>
    /// <para>
    /// When two handlers target the same <see cref="Proto.MessageType"/>, the last registered
    /// handler takes precedence.
    /// </para>
    /// </remarks>
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

        services.TryAddSingleton<EntglDb.Network.Telemetry.INetworkTelemetryService>(sp => 
        {
            var logger = sp.GetRequiredService<ILogger<EntglDb.Network.Telemetry.NetworkTelemetryService>>();
            var path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "entgldb_metrics.bin");
            return new EntglDb.Network.Telemetry.NetworkTelemetryService(logger, path);
        });

        // Register built-in core message handlers. Each is a separate INetworkMessageHandler
        // implementation so that TcpSyncServer has no message-type-specific logic.
        services.AddSingleton<INetworkMessageHandler, GetClockHandler>();
        services.AddSingleton<INetworkMessageHandler, GetVectorClockHandler>();
        services.AddSingleton<INetworkMessageHandler, PullChangesHandler>();
        services.AddSingleton<INetworkMessageHandler, PushChangesHandler>();
        services.AddSingleton<INetworkMessageHandler, GetChainRangeHandler>();
        services.AddSingleton<INetworkMessageHandler, GetSnapshotHandler>();

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
