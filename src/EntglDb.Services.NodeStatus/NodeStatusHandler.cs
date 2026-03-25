using System;
using System.Linq;
using System.Threading.Tasks;
using EntglDb.Core.Network;
using EntglDb.Network;
using EntglDb.Services.NodeStatus.Proto;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace EntglDb.Services.NodeStatus;

/// <summary>
/// Server-side handler that responds to <see cref="NodeStatusMessageType.NodeStatusReq"/> (wire type 1000)
/// with a snapshot of the local node's runtime status.
/// </summary>
/// <remarks>
/// Register this handler in DI <em>after</em> <c>AddEntglDbNetwork</c>:
/// <code>
/// services.AddEntglDbNetwork&lt;MyConfig&gt;();
/// services.AddEntglDbNodeStatus();   // registers NodeStatusHandler + INodeStatusService
/// </code>
/// </remarks>
public sealed class NodeStatusHandler : INetworkMessageHandler
{
    private readonly IPeerNodeConfigurationProvider _configProvider;
    private readonly IDiscoveryService _discovery;
    private readonly ILogger<NodeStatusHandler> _logger;
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;

    // Assembly version is resolved once at construction time.
    private static readonly string ServiceVersion =
        typeof(NodeStatusHandler).Assembly.GetName().Version?.ToString() ?? "unknown";

    public int MessageType => (int)NodeStatusMessageType.NodeStatusReq;

    public NodeStatusHandler(
        IPeerNodeConfigurationProvider configProvider,
        IDiscoveryService discovery,
        ILogger<NodeStatusHandler> logger)
    {
        _configProvider = configProvider;
        _discovery = discovery;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<(IMessage? Response, int ResponseType)> HandleAsync(IMessageHandlerContext context)
    {
        var config = await _configProvider.GetConfiguration();

        var activePeers = _discovery.GetActivePeers().ToList();

        var response = new NodeStatusResponse
        {
            NodeId = config.NodeId,
            UptimeSeconds = (long)(DateTimeOffset.UtcNow - _startTime).TotalSeconds,
            KnownPeerCount = activePeers.Count,
            ServiceVersion = ServiceVersion,
        };

        foreach (var peer in activePeers)
            response.KnownPeerAddresses.Add(peer.Address);

        _logger.LogDebug("NodeStatus query from {Remote}: responded with {PeerCount} known peers",
            context.RemoteEndPoint, activePeers.Count);

        return (response, (int)NodeStatusMessageType.NodeStatusRes);
    }
}
