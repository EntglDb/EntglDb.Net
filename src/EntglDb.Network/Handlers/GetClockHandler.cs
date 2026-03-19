using EntglDb.Core.Storage;
using EntglDb.Network.Proto;
using Google.Protobuf;
using System.Threading.Tasks;

namespace EntglDb.Network.Handlers;

/// <summary>
/// Handles <see cref="MessageType.GetClockReq"/> by returning the latest HLC timestamp from the oplog.
/// </summary>
internal sealed class GetClockHandler : INetworkMessageHandler
{
    private readonly IOplogStore _oplogStore;

    public GetClockHandler(IOplogStore oplogStore)
    {
        _oplogStore = oplogStore;
    }

    public MessageType MessageType => MessageType.GetClockReq;

    public async Task<(IMessage? Response, MessageType ResponseType)> HandleAsync(IMessageHandlerContext context)
    {
        var clock = await _oplogStore.GetLatestTimestampAsync(context.CancellationToken);
        return (new ClockResponse
        {
            HlcWall = clock.PhysicalTime,
            HlcLogic = clock.LogicalCounter,
            HlcNode = clock.NodeId
        }, MessageType.ClockRes);
    }
}
