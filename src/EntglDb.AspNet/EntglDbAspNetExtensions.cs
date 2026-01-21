using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using EntglDb.AspNet.Configuration;
using EntglDb.AspNet.Services;
using EntglDb.AspNet.HealthChecks;
using EntglDb.AspNet.HostedServices;
using EntglDb.Core.Storage;
using EntglDb.Core.Network;
using EntglDb.Network;
using EntglDb.Network.Security;

namespace EntglDb.AspNet;

/// <summary>
/// Extension methods for configuring EntglDb in ASP.NET Core applications.
/// </summary>
public static class EntglDbAspNetExtensions
{
    /// <summary>
    /// Adds EntglDb ASP.NET integration with the specified configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure EntglDb options.</param>
    public static IServiceCollection AddEntglDbAspNet(
        this IServiceCollection services,
        Action<EntglDbAspNetOptions> configure)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (configure == null) throw new ArgumentNullException(nameof(configure));

        var options = new EntglDbAspNetOptions();
        configure(options);

        // Register options
        services.TryAddSingleton(options);

        // Register services based on mode
        if (options.Mode == ServerMode.SingleCluster)
        {
            RegisterSingleClusterServices(services, options.SingleCluster);
        }
        else
        {
            RegisterMultiClusterServices(services, options.MultiCluster);
        }

        // Register common services
        RegisterCommonServices(services, options);

        return services;
    }

    /// <summary>
    /// Adds EntglDb ASP.NET integration for single-cluster mode.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure single-cluster options.</param>
    public static IServiceCollection AddEntglDbAspNetSingleCluster(
        this IServiceCollection services,
        Action<SingleClusterOptions>? configure = null)
    {
        return services.AddEntglDbAspNet(options =>
        {
            options.Mode = ServerMode.SingleCluster;
            configure?.Invoke(options.SingleCluster);
        });
    }

    /// <summary>
    /// Adds EntglDb ASP.NET integration for multi-cluster mode.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure multi-cluster options.</param>
    public static IServiceCollection AddEntglDbAspNetMultiCluster(
        this IServiceCollection services,
        Action<MultiClusterOptions> configure)
    {
        if (configure == null) throw new ArgumentNullException(nameof(configure));

        return services.AddEntglDbAspNet(options =>
        {
            options.Mode = ServerMode.MultiCluster;
            configure(options.MultiCluster);
        });
    }

    private static void RegisterSingleClusterServices(
        IServiceCollection services,
        SingleClusterOptions options)
    {
        // Discovery service (no-op in server mode - no UDP broadcast)
        services.TryAddSingleton<IDiscoveryService, NoOpDiscoveryService>();

        // Sync orchestrator - use actual orchestrator to propagate changes between peers
        // Cloud nodes need to act as propagators for scenarios:
        // 1. Services connected to cloud that modify data
        // 2. Separate LAN clusters that connect through the cloud
        services.TryAddSingleton<ISyncOrchestrator, SyncOrchestrator>();

        // Hosted services
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TcpSyncServerHostedService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DiscoveryServiceHostedService>());
    }

    private static void RegisterMultiClusterServices(
        IServiceCollection services,
        MultiClusterOptions options)
    {
        // Multi-cluster mode uses the same services as single-cluster
        // but with different configuration for routing
        
        // Discovery service (no-op in server mode - no UDP broadcast)
        services.TryAddSingleton<IDiscoveryService, NoOpDiscoveryService>();

        // Sync orchestrator - use actual orchestrator to propagate changes between peers
        // Cloud nodes need to act as propagators for scenarios:
        // 1. Services connected to cloud that modify data
        // 2. Separate LAN clusters that connect through the cloud
        services.TryAddSingleton<ISyncOrchestrator, SyncOrchestrator>();

        // Note: Multi-cluster TCP routing would be implemented here
        // For now, we use the same hosted services
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TcpSyncServerHostedService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DiscoveryServiceHostedService>());
    }

    private static void RegisterCommonServices(
        IServiceCollection services,
        EntglDbAspNetOptions options)
    {
        // Health checks
        if (options.EnableHealthChecks)
        {
            services.AddHealthChecks()
                .AddCheck<EntglDbHealthCheck>(
                    "entgldb",
                    failureStatus: HealthStatus.Unhealthy,
                    tags: new[] { "db", "ready" });
        }
    }
}
