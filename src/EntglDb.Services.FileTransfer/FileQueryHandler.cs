using System.Threading.Tasks;
using EntglDb.Network;
using EntglDb.Services.FileTransfer.Proto;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace EntglDb.Services.FileTransfer;

/// <summary>
/// Handles incoming <see cref="FileTransferMessageType.FileQueryReq"/> (wire type 1100) messages.
/// Returns metadata about the requested file, or a not-found response.
/// </summary>
internal sealed class FileQueryHandler : INetworkMessageHandler
{
    private readonly IFileProvider _fileProvider;
    private readonly ILogger<FileQueryHandler> _logger;

    public int MessageType => (int)FileTransferMessageType.FileQueryReq;

    public FileQueryHandler(IFileProvider fileProvider, ILogger<FileQueryHandler> logger)
    {
        _fileProvider = fileProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<(IMessage? Response, int ResponseType)> HandleAsync(IMessageHandlerContext context)
    {
        var request = FileQueryRequest.Parser.ParseFrom(context.Payload);
        var info = await _fileProvider.GetInfoAsync(request.FileId, context.CancellationToken);

        if (info is null)
        {
            _logger.LogDebug("FileQuery: '{FileId}' not found (requested by {Remote})",
                request.FileId, context.RemoteEndPoint);

            return (new FileQueryResponse { Found = false }, (int)FileTransferMessageType.FileQueryRes);
        }

        _logger.LogDebug("FileQuery: '{FileId}' found ({Bytes} bytes, requested by {Remote})",
            request.FileId, info.SizeBytes, context.RemoteEndPoint);

        return (new FileQueryResponse
        {
            Found           = true,
            FileId          = info.FileId,
            FileName        = info.FileName,
            SizeBytes       = info.SizeBytes,
            Sha256Checksum  = info.Sha256Checksum ?? string.Empty,
        }, (int)FileTransferMessageType.FileQueryRes);
    }
}
