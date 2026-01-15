using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Core.Network; // For IMeshNetwork if we implement it

namespace EntglDb.Network
{
    public static class EntglDbNetworkExtensions
    {
        public static IServiceCollection AddEntglDbNetwork(this IServiceCollection services, string nodeId, int tcpPort, string authToken, bool useLocalhost = false)
        {
            services.AddSingleton<EntglDb.Network.Security.IAuthenticator>(new EntglDb.Network.Security.ClusterKeyAuthenticator(authToken));

            services.AddSingleton<UdpDiscoveryService>(sp => 
                new UdpDiscoveryService(nodeId, tcpPort, sp.GetRequiredService<ILogger<UdpDiscoveryService>>(), useLocalhost));

            services.AddSingleton<TcpSyncServer>(sp => 
                new TcpSyncServer(tcpPort, sp.GetRequiredService<IPeerStore>(), nodeId, sp.GetRequiredService<ILogger<TcpSyncServer>>(), sp.GetRequiredService<EntglDb.Network.Security.IAuthenticator>()));

            services.AddSingleton<SyncOrchestrator>(sp =>
                new SyncOrchestrator(
                    sp.GetRequiredService<UdpDiscoveryService>(),
                    sp.GetRequiredService<IPeerStore>(),
                    nodeId,
                    authToken,
                    sp.GetRequiredService<ILoggerFactory>()
                ));

            // Facade for single-point control
            services.AddSingleton<EntglDbNode>(sp => 
                new EntglDbNode(
                    sp.GetRequiredService<TcpSyncServer>(),
                    sp.GetRequiredService<UdpDiscoveryService>(),
                    sp.GetRequiredService<SyncOrchestrator>(),
                    sp.GetRequiredService<ILogger<EntglDbNode>>()
                ));

            return services;
        }
    }
}
