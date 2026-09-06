using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace EntglDb.Network;

/// <summary>
/// Represents a single EntglDb Peer Node.
/// Acts as a facade to orchestrate the lifecycle of Networking, Discovery, and Synchronization components.
/// </summary>
/// <remarks>
/// The transport half - server, discovery, address - is <see cref="NetworkNode"/>'s; this adds document sync
/// on top. A consumer that only needs the peer-to-peer transport registers <see cref="INetworkNode"/> instead
/// (<c>AddEntglDbNetworkNode()</c>) and never pulls in this package.
/// </remarks>
public class EntglDbNode : IEntglDbNode
{
    private readonly ILogger<EntglDbNode> _logger;

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
        ).ConfigureAwait(false);

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
        ).ConfigureAwait(false);

        _logger.LogInformation("EntglDb Node Stopped.");
    }

    /// <summary>
    /// Gets the address information of this node.
    /// </summary>
    public NodeAddress Address => NetworkNode.ResolveAddress(Server, _logger);
}
