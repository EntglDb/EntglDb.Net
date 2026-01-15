using System;
using Microsoft.Extensions.Logging;

namespace EntglDb.Network
{
    /// <summary>
    /// Represents a single EntglDb Peer Node. 
    /// Acts as a facade to orchestrate the lifecycle of Networking, Discovery, and Synchronization components.
    /// </summary>
    public class EntglDbNode
    {
        /// <summary>
        /// Gets the TCP Sync Server instance.
        /// </summary>
        public TcpSyncServer Server { get; }

        /// <summary>
        /// Gets the UDP Discovery Service instance.
        /// </summary>
        public UdpDiscoveryService Discovery { get; }

        /// <summary>
        /// Gets the Synchronization Orchestrator instance.
        /// </summary>
        public SyncOrchestrator Orchestrator { get; }

        private readonly ILogger<EntglDbNode> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="EntglDbNode"/> class.
        /// </summary>
        /// <param name="server">The TCP server for handling incoming sync requests.</param>
        /// <param name="discovery">The UDP service for peer discovery.</param>
        /// <param name="orchestrator">The orchestrator for managing outgoing sync operations.</param>
        /// <param name="logger">The logger instance.</param>
        public EntglDbNode(
            TcpSyncServer server, 
            UdpDiscoveryService discovery, 
            SyncOrchestrator orchestrator,
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
        public void Start()
        {
            _logger.LogInformation("Starting EntglDb Node...");
            
            Server.Start();
            Discovery.Start();
            Orchestrator.Start();
            
            _logger.LogInformation("EntglDb Node Started.");
        }

        /// <summary>
        /// Stops all node components.
        /// </summary>
        public void Stop()
        {
            _logger.LogInformation("Stopping EntglDb Node...");
            
            Orchestrator.Stop();
            Discovery.Stop();
            Server.Stop();
        }
    }
}
