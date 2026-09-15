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

        /// <summary>169.254.0.0/16, the address an interface self-assigns when no DHCP answered.</summary>
        private static bool IsLinkLocal(IPAddress address)
        {
            var octets = address.GetAddressBytes();
            return octets[0] == 169 && octets[1] == 254;
        }

        /// <summary>
        /// The address this node advertises to its peers.
        /// </summary>
        /// <remarks>
        /// Interfaces with a gateway come first, and link-local (169.254/16) addresses are never used.
        /// An APIPA address is what an interface gets when nobody assigned it one - a second NIC with no
        /// cable, a phone or tablet plugged in over USB, a virtual adapter - and it is unreachable from
        /// the LAN the other nodes are on. Advertising one makes this node look present and answer
        /// nothing, which is worse than not being discovered at all: taking the first address of the
        /// first interface let whichever order the OS returned decide.
        /// </remarks>
        private static string ResolveLocalIpAddress(ILogger logger)
        {
            try
            {
                var candidates = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(i => i.OperationalStatus == OperationalStatus.Up
                             && i.NetworkInterfaceType != NetworkInterfaceType.Loopback
                             && i.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .Select(i => new
                    {
                        HasGateway = i.GetIPProperties().GatewayAddresses
                            .Any(g => g.Address?.AddressFamily == AddressFamily.InterNetwork
                                   && !g.Address.Equals(IPAddress.Any)),
                        Addresses = i.GetIPProperties().UnicastAddresses
                            .Select(u => u.Address)
                            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IsLinkLocal(a))
                            .ToList()
                    })
                    .Where(i => i.Addresses.Count > 0)
                    .OrderByDescending(i => i.HasGateway)
                    .ToList();

                var chosen = candidates.FirstOrDefault()?.Addresses.FirstOrDefault();
                if (chosen != null)
                {
                    return chosen.ToString();
                }

                logger.LogWarning(
                    "No routable IPv4 address found on any interface; falling back to localhost. " +
                    "Peers on other machines will not be able to reach this node.");

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
