using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace EntglDb.Network
{
    /// <summary>
    /// A peer that serves requests and discovers others, and nothing more - see <see cref="INetworkNode"/>.
    /// </summary>
    public class NetworkNode : INetworkNode
    {
        private readonly ILogger<NetworkNode> _logger;

        /// <inheritdoc/>
        public ISyncServer Server { get; }

        /// <inheritdoc/>
        public IDiscoveryService Discovery { get; }

        public NetworkNode(ISyncServer server, IDiscoveryService discovery, ILogger<NetworkNode> logger)
        {
            Server = server;
            Discovery = discovery;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task Start()
        {
            _logger.LogInformation("Starting network node...");

            await Task.WhenAll(
                Server.Start(),
                Discovery.Start()
            ).ConfigureAwait(false);

            _logger.LogInformation("Network node started on {Address}", Address);
        }

        /// <inheritdoc/>
        public async Task Stop()
        {
            _logger.LogInformation("Stopping network node...");

            await Task.WhenAll(
                Discovery.Stop(),
                Server.Stop()
            ).ConfigureAwait(false);

            _logger.LogInformation("Network node stopped.");
        }

        /// <inheritdoc/>
        public NodeAddress Address => ResolveAddress(Server, _logger);

        /// <summary>
        /// Shared with <c>EntglDbNode</c> so both node facades advertise the same address for the same
        /// server.
        /// </summary>
        internal static NodeAddress ResolveAddress(ISyncServer server, ILogger logger)
        {
            var endpoint = server.ListeningEndpoint;
            if (endpoint == null)
            {
                return new NodeAddress("Unknown", 0);
            }

            // A server listening on Any (0.0.0.0) has no address a peer could dial - the machine's own
            // routable address has to be resolved instead.
            if (Equals(endpoint.Address, IPAddress.Any) || Equals(endpoint.Address, IPAddress.IPv6Any))
            {
                return new NodeAddress(ResolveLocalIpAddress(logger), endpoint.Port);
            }

            return new NodeAddress(endpoint.Address.ToString(), endpoint.Port);
        }

        private static string ResolveLocalIpAddress(ILogger logger)
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(i => i.OperationalStatus == OperationalStatus.Up
                             && i.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                foreach (var candidate in interfaces)
                {
                    var address = candidate.GetIPProperties().UnicastAddresses
                        .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);

                    if (address != null)
                    {
                        return address.Address.ToString();
                    }
                }

                return "127.0.0.1";
            }
            catch (Exception ex)
            {
                logger.LogWarning("Failed to resolve local IP: {Message}. Falling back to localhost.", ex.Message);
                return "127.0.0.1";
            }
        }
    }

    /// <summary>The host and port a peer is reachable at.</summary>
    public class NodeAddress
    {
        public string Host { get; }
        public int Port { get; }

        public NodeAddress(string host, int port)
        {
            Host = host;
            Port = port;
        }

        public override string ToString() => $"{Host}:{Port}";
    }
}
