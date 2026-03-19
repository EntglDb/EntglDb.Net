using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Network.Proto;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace EntglDb.Network.Handlers;

/// <summary>
/// Handles <see cref="MessageType.PushChangesReq"/> by applying a batch of oplog entries from a remote peer.
/// </summary>
internal sealed class PushChangesHandler : INetworkMessageHandler
{
    private readonly IOplogStore _oplogStore;
    private readonly ILogger<PushChangesHandler> _logger;

    public PushChangesHandler(IOplogStore oplogStore, ILogger<PushChangesHandler> logger)
    {
        _oplogStore = oplogStore;
        _logger = logger;
    }

    public MessageType MessageType => MessageType.PushChangesReq;

    public async Task<(IMessage? Response, MessageType ResponseType)> HandleAsync(IMessageHandlerContext context)
    {
        var pushReq = PushChangesRequest.Parser.ParseFrom(context.Payload);
        var entries = new List<OplogEntry>();

        foreach (var e in pushReq.Entries)
        {
            if (!Enum.TryParse<OperationType>(e.Operation, ignoreCase: true, out var operation))
            {
                _logger.LogWarning("Failed to parse OperationType from value '{Operation}' in PushChangesReq.", e.Operation);
                return (new AckResponse { Success = false }, MessageType.AckRes);
            }

            entries.Add(new OplogEntry(
                e.Collection,
                e.Key,
                operation,
                string.IsNullOrEmpty(e.JsonData) ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(e.JsonData),
                new HlcTimestamp(e.HlcWall, e.HlcLogic, e.HlcNode),
                e.PreviousHash,
                e.Hash
            ));
        }

        await _oplogStore.ApplyBatchAsync(entries, context.CancellationToken);

        return (new AckResponse { Success = true }, MessageType.AckRes);
    }
}
