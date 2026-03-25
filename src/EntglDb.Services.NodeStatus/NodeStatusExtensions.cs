using EntglDb.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EntglDb.Services.NodeStatus;

/// <summary>
/// Extension methods for registering EntglDb node-status diagnostic services.
/// </summary>
public static class NodeStatusExtensions
{
    /// <summary>
    /// Adds the EntglDb node-status service to the DI container.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registers:
    /// <list type="bullet">
    ///   <item><see cref="NodeStatusHandler"/> as <see cref="INetworkMessageHandler"/> (server-side responder, wire type 1000)</item>
    ///   <item><see cref="INodeStatusService"/> / <see cref="NodeStatusClient"/> (client-side query API)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Call <c>AddEntglDbNetwork&lt;T&gt;()</c> before this method so that
    /// <see cref="IPeerMessenger"/> is already registered.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddEntglDbNodeStatus(this IServiceCollection services)
    {
        // Server side: handle incoming NodeStatusRequest messages (wire type 1000)
        services.AddSingleton<INetworkMessageHandler, NodeStatusHandler>();

        // Client side: let consumers query remote peers
        services.TryAddSingleton<INodeStatusService, NodeStatusClient>();

        return services;
    }
}
