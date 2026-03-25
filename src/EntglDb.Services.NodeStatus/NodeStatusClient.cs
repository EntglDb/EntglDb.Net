using System;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Network;
using EntglDb.Services.NodeStatus.Proto;

namespace EntglDb.Services.NodeStatus;

/// <summary>
/// Default implementation of <see cref="INodeStatusService"/>.
/// Uses <see cref="IPeerMessenger"/> to send a <see cref="NodeStatusRequest"/> to the remote peer
/// and parse the <see cref="NodeStatusResponse"/> back as a <see cref="NodeStatusInfo"/>.
/// </summary>
internal sealed class NodeStatusClient : INodeStatusService
{
    private readonly IPeerMessenger _messenger;

    public NodeStatusClient(IPeerMessenger messenger)
    {
        _messenger = messenger;
    }

    /// <inheritdoc/>
    public async Task<NodeStatusInfo> QueryAsync(string peerAddress, CancellationToken ct = default)
    {
        var (responseType, payload) = await _messenger.SendAndReceiveAsync(
            peerAddress,
            (int)NodeStatusMessageType.NodeStatusReq,
            new NodeStatusRequest(),
            ct);

        if (responseType != (int)NodeStatusMessageType.NodeStatusRes)
            throw new InvalidOperationException(
                $"Unexpected response type {responseType} from peer '{peerAddress}'.");

        var proto = NodeStatusResponse.Parser.ParseFrom(payload);

        return new NodeStatusInfo
        {
            NodeId = proto.NodeId,
            Uptime = TimeSpan.FromSeconds(proto.UptimeSeconds),
            KnownPeerAddresses = proto.KnownPeerAddresses,
        };
    }
}
