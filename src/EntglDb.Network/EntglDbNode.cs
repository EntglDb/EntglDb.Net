using System;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;

namespace EntglDb.Network;

/// <summary>
/// Represents a single EntglDb Peer Node. 
/// Acts as a facade to orchestrate the lifecycle of Networking, Discovery, and Synchronization components.
/// </summary>
public class EntglDbNode : IEntglDbNode
{
    /// <summary>
    /// Gets the Sync Server instance.
    /// </summary>
    public ISyncServer Server { get; }

    /// <summary>
    /// Gets the Discovery Service instance.
    /// </summary>
    public IDiscoveryService Discovery { get; }

    /// <summary>
    /// Gets the Synchronization Orchestrator instance.
    /// </summary>
    public ISyncOrchestrator Orchestrator { get; }

    private readonly ILogger<EntglDbNode> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntglDbNode"/> class.
    /// </summary>
    /// <param name="server">The TCP server for handling incoming sync requests.</param>
    /// <param name="discovery">The UDP service for peer discovery.</param>
    /// <param name="orchestrator">The orchestrator for managing outgoing sync operations.</param>
    /// <param name="logger">The logger instance.</param>
    public EntglDbNode(
        ISyncServer server,
        IDiscoveryService discovery,
        ISyncOrchestrator orchestrator,
        ILogger<EntglDbNode> logger)
    {
        Server = server;
        Discovery = discovery;
        Orchestrator = orchestrator;
        _logger = logger;
    }

    /// <summary>
    /// Starts all node components (Server, Discovery, Orchestrator).
    /// </summary>
    public async Task Start()
    {
        _logger.LogInformation("Starting EntglDb Node...");

        await Task.WhenAll(
            Server.Start(),
            Discovery.Start(),
            Orchestrator.Start()
        );

        _logger.LogInformation("EntglDb Node Started on {Address}", Address);
    }

    /// <summary>
    /// Stops all node components.
    /// </summary>
    public async Task Stop()
    {
        _logger.LogInformation("Stopping EntglDb Node...");

        await Task.WhenAll(
            Orchestrator.Stop(),
            Discovery.Stop(),
            Server.Stop()
        );

        _logger.LogInformation("EntglDb Node Stopped.");
    }

    /// <summary>
    /// Gets the address information of this node.
    /// </summary>
    public NodeAddress Address
    {
        get
        {
            var ep = Server.ListeningEndpoint;
            if (ep != null)
            {
                // If the server is listening on "Any" (0.0.0.0), we cannot advertise that as a connectable address.
                // We must resolve the actual machine IP address that peers can reach.
                if (Equals(ep.Address, System.Net.IPAddress.Any) || Equals(ep.Address, System.Net.IPAddress.IPv6Any))
                {
                    return new NodeAddress(GetLocalIpAddress(), ep.Port);
                }
                return new NodeAddress(ep.Address.ToString(), ep.Port);
            }
            return new NodeAddress("Unknown", 0);
        }
    }

    private string GetLocalIpAddress()
    {
        try
        {
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(i => i.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                      && i.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback);

            foreach (var i in interfaces)
            {
                var props = i.GetIPProperties();
                var ipInfo = props.UnicastAddresses
                    .FirstOrDefault(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork); // Prefer IPv4

                if (ipInfo != null)
                {
                    return ipInfo.Address.ToString();
                }
            }

            return "127.0.0.1";
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to resolve local IP: {Message}. Fallback to localhost.", ex.Message);
            return "127.0.0.1";
        }
    }
}

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
