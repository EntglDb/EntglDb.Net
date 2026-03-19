using EntglDb.Core.Storage;
using EntglDb.Network.Proto;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;

namespace EntglDb.Network.Handlers;

/// <summary>
/// Handles <see cref="MessageType.GetSnapshotReq"/> by streaming the full database snapshot
/// to the requesting peer in <see cref="MessageType.SnapshotChunkMsg"/> chunks.
/// Returns <c>(null, MessageType.Unknown)</c> because the response is sent directly via
/// <see cref="IMessageHandlerContext.SendMessageAsync"/>.
/// </summary>
internal sealed class GetSnapshotHandler : INetworkMessageHandler
{
    private const int ChunkSizeBytes = 80 * 1024; // 80 KB

    private readonly ISnapshotService _snapshotService;
    private readonly ILogger<GetSnapshotHandler> _logger;

    public GetSnapshotHandler(ISnapshotService snapshotService, ILogger<GetSnapshotHandler> logger)
    {
        _snapshotService = snapshotService;
        _logger = logger;
    }

    public MessageType MessageType => MessageType.GetSnapshotReq;

    public async Task<(IMessage? Response, MessageType ResponseType)> HandleAsync(IMessageHandlerContext context)
    {
        _logger.LogInformation("Processing GetSnapshotReq from {Endpoint}", context.RemoteEndPoint);
        var tempFile = Path.GetTempFileName();
        try
        {
            using (var fs = File.Create(tempFile))
            {
                await _snapshotService.CreateSnapshotAsync(fs, context.CancellationToken);
            }

            using (var fs = File.OpenRead(tempFile))
            {
                byte[] buffer = new byte[ChunkSizeBytes];
                int bytesRead;
                while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length, context.CancellationToken)) > 0)
                {
                    var chunk = new SnapshotChunk
                    {
                        Data = ByteString.CopyFrom(buffer, 0, bytesRead),
                        IsLast = false
                    };
                    await context.SendMessageAsync(MessageType.SnapshotChunkMsg, chunk);
                }

                // Signal end of snapshot
                await context.SendMessageAsync(MessageType.SnapshotChunkMsg, new SnapshotChunk { IsLast = true });
            }
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
        return (null, MessageType.Unknown);
    }
}
