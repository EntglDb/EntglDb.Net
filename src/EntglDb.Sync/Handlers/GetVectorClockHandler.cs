using EntglDb.Core.Storage;
using EntglDb.Sync.Proto;
using Google.Protobuf;
using System.Threading.Tasks;

namespace EntglDb.Network.Handlers;

/// <summary>
/// Handles <see cref="SyncMessageType.GetVectorClockReq"/> by returning the full vector clock from the oplog.
/// </summary>
internal sealed class GetVectorClockHandler : INetworkMessageHandler
{
    private readonly IOplogStore _oplogStore;

    public GetVectorClockHandler(IOplogStore oplogStore)
    {
        _oplogStore = oplogStore;
    }

    public int MessageType => (int)SyncMessageType.GetVectorClockReq;

    public async Task<(IMessage? Response, int ResponseType)> HandleAsync(IMessageHandlerContext context)
    {
        var vectorClock = await _oplogStore.GetVectorClockAsync(context.CancellationToken);
        var vcRes = new VectorClockResponse();
        foreach (var nodeId in vectorClock.NodeIds)
        {
            var ts = vectorClock.GetTimestamp(nodeId);
            vcRes.Entries.Add(new VectorClockEntry
            {
                NodeId = nodeId,
                HlcWall = ts.PhysicalTime,
                HlcLogic = ts.LogicalCounter
            });
        }
        return (vcRes, (int)SyncMessageType.VectorClockRes);
    }
}
