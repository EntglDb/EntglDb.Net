using EntglDb.Network;
using EntglDb.Network.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace EntglDb.Sync;

/// <summary>
/// Extension methods for registering EntglDb synchronization services in the DI container.
/// </summary>
public static class SyncExtensions
{
    /// <summary>
    /// Adds EntglDb synchronization services to the service collection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registers the six core sync message handlers (<c>GetClockHandler</c>,
    /// <c>GetVectorClockHandler</c>, <c>PullChangesHandler</c>,
    /// <c>PushChangesHandler</c>, <c>GetChainRangeHandler</c>, <c>GetSnapshotHandler</c>),
    /// <see cref="ISyncOrchestrator"/>, <see cref="IEntglDbNode"/>,
    /// and optionally <see cref="EntglDbNodeService"/> as a hosted service.
    /// </para>
    /// <para>
    /// Call <c>AddEntglDbNetwork&lt;TConfig&gt;()</c> before calling this method.
    /// </para>
    /// </remarks>
    /// <param name="useHostedService">
    /// If <c>true</c> (default), registers <see cref="EntglDbNodeService"/> as an <see cref="IHostedService"/>
    /// to automatically start and stop the node with the application.
    /// </param>
    public static IServiceCollection AddEntglDbSync(
        this IServiceCollection services,
        bool useHostedService = true)
    {
        // Register built-in sync message handlers
        services.AddSingleton<INetworkMessageHandler, GetClockHandler>();
        services.AddSingleton<INetworkMessageHandler, GetVectorClockHandler>();
        services.AddSingleton<INetworkMessageHandler, PullChangesHandler>();
        services.AddSingleton<INetworkMessageHandler, PushChangesHandler>();
        services.AddSingleton<INetworkMessageHandler, GetChainRangeHandler>();
        services.AddSingleton<INetworkMessageHandler, GetSnapshotHandler>();

        services.TryAddSingleton<ISyncOrchestrator, SyncOrchestrator>();
        services.TryAddSingleton<IEntglDbNode, EntglDbNode>();

        if (useHostedService)
        {
            services.AddHostedService<EntglDbNodeService>();
        }

        return services;
    }
}
