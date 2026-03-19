using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Network.Proto;
using Google.Protobuf;
using System.Linq;
using System.Threading.Tasks;

namespace EntglDb.Network.Handlers;

/// <summary>
/// Handles <see cref="MessageType.PullChangesReq"/> by returning oplog entries since the requested timestamp.
/// </summary>
internal sealed class PullChangesHandler : INetworkMessageHandler
{
    private readonly IOplogStore _oplogStore;

    public PullChangesHandler(IOplogStore oplogStore)
    {
        _oplogStore = oplogStore;
    }

    public MessageType MessageType => MessageType.PullChangesReq;

    public async Task<(IMessage? Response, MessageType ResponseType)> HandleAsync(IMessageHandlerContext context)
    {
        var pReq = PullChangesRequest.Parser.ParseFrom(context.Payload);
        var since = new HlcTimestamp(pReq.SinceWall, pReq.SinceLogic, pReq.SinceNode);

        // Use collection filter from request
        var filter = pReq.Collections.Any() ? pReq.Collections : null;
        var oplog = await _oplogStore.GetOplogAfterAsync(since, filter, context.CancellationToken);

        var csRes = new ChangeSetResponse();
        foreach (var e in oplog)
        {
            csRes.Entries.Add(new ProtoOplogEntry
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
        return (csRes, MessageType.ChangeSetRes);
    }
}
