using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Network.Proto;
using Google.Protobuf;
using System.Linq;
using System.Threading.Tasks;

namespace EntglDb.Network.Handlers;

/// <summary>
/// Handles <see cref="MessageType.GetChainRangeReq"/> by returning oplog entries between two chain hashes.
/// Returns a <see cref="ChainRangeResponse"/> with <see cref="ChainRangeResponse.SnapshotRequired"/> set
/// to <c>true</c> when the requested range cannot be filled (e.g. pruned chain).
/// </summary>
internal sealed class GetChainRangeHandler : INetworkMessageHandler
{
    private readonly IOplogStore _oplogStore;

    public GetChainRangeHandler(IOplogStore oplogStore)
    {
        _oplogStore = oplogStore;
    }

    public MessageType MessageType => MessageType.GetChainRangeReq;

    public async Task<(IMessage? Response, MessageType ResponseType)> HandleAsync(IMessageHandlerContext context)
    {
        var rangeReq = GetChainRangeRequest.Parser.ParseFrom(context.Payload);
        var rangeEntries = await _oplogStore.GetChainRangeAsync(rangeReq.StartHash, rangeReq.EndHash, context.CancellationToken);
        var rangeRes = new ChainRangeResponse();

        if (!rangeEntries.Any() && rangeReq.StartHash != rangeReq.EndHash)
        {
            // Gap cannot be filled (likely pruned or unknown branch)
            rangeRes.SnapshotRequired = true;
        }
        else
        {
            foreach (var e in rangeEntries)
            {
                rangeRes.Entries.Add(new ProtoOplogEntry
                {
                    Collection = e.Collection,
                    Key = e.Key,
                    Operation = e.Operation.ToString(),
                    JsonData = e.Payload?.GetRawText() ?? "",
                    HlcWall = e.Timestamp.PhysicalTime,
                    HlcLogic = e.Timestamp.LogicalCounter,
                    HlcNode = e.Timestamp.NodeId,
                    Hash = e.Hash,
                    PreviousHash = e.PreviousHash
                });
            }
        }
        return (rangeRes, MessageType.ChainRangeRes);
    }
}
