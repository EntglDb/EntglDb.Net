using EntglDb.Core.Storage;
using EntglDb.Sync.Proto;
using Google.Protobuf;
using System.Threading.Tasks;

namespace EntglDb.Network.Handlers;

/// <summary>
/// Handles <see cref="SyncMessageType.GetClockReq"/> by returning the latest HLC timestamp from the oplog.
/// </summary>
internal sealed class GetClockHandler : INetworkMessageHandler
{
    private readonly IOplogStore _oplogStore;

    public GetClockHandler(IOplogStore oplogStore)
    {
        _oplogStore = oplogStore;
    }

    public int MessageType => (int)SyncMessageType.GetClockReq;

    public async Task<(IMessage? Response, int ResponseType)> HandleAsync(IMessageHandlerContext context)
    {
        var clock = await _oplogStore.GetLatestTimestampAsync(context.CancellationToken);
        return (new ClockResponse
        {
            HlcWall = clock.PhysicalTime,
            HlcLogic = clock.LogicalCounter,
            HlcNode = clock.NodeId
        }, (int)SyncMessageType.ClockRes);
    }
}
