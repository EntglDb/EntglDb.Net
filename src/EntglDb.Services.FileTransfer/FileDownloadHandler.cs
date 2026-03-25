using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using EntglDb.Network;
using EntglDb.Services.FileTransfer.Proto;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace EntglDb.Services.FileTransfer;

/// <summary>
/// Handles incoming <see cref="FileTransferMessageType.FileDownloadReq"/> (wire type 1102) messages.
/// Streams the file as a sequence of <see cref="FileChunkMessage"/> messages, then sends
/// a <see cref="FileTransferCompleteMessage"/> with the SHA-256 checksum.
/// </summary>
/// <remarks>
/// The handler uses <c>context.SendMessageAsync</c> directly (streaming pattern) and returns
/// <c>(null, 0)</c> to signal that the response has already been written to the wire.
/// </remarks>
internal sealed class FileDownloadHandler : INetworkMessageHandler
{
    // 64 KB chunks — balances memory usage against per-message overhead.
    private const int ChunkSize = 64 * 1024;

    private readonly IFileProvider _fileProvider;
    private readonly ILogger<FileDownloadHandler> _logger;

    public int MessageType => (int)FileTransferMessageType.FileDownloadReq;

    public FileDownloadHandler(IFileProvider fileProvider, ILogger<FileDownloadHandler> logger)
    {
        _fileProvider = fileProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<(IMessage? Response, int ResponseType)> HandleAsync(IMessageHandlerContext context)
    {
        var request = FileDownloadRequest.Parser.ParseFrom(context.Payload);
        var ct = context.CancellationToken;

        var fileStream = await _fileProvider.OpenReadAsync(request.FileId, ct);
        if (fileStream is null)
        {
            _logger.LogWarning("FileDownload: '{FileId}' not found (requested by {Remote})",
                request.FileId, context.RemoteEndPoint);

            // Signal failure via a done-message with 0 chunks and an empty checksum,
            // which the client will reject as an integrity error.
            await context.SendMessageAsync(
                (int)FileTransferMessageType.FileTransferDoneMsg,
                new FileTransferCompleteMessage { TotalChunks = -1, Sha256Checksum = string.Empty });

            return (null, 0);
        }

        _logger.LogInformation("FileDownload: starting '{FileId}' → {Remote}",
            request.FileId, context.RemoteEndPoint);

        await using (fileStream)
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            var buffer = new byte[ChunkSize];
            int chunkIndex = 0;
            int bytesRead;

            while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                hasher.AppendData(buffer, 0, bytesRead);

                await context.SendMessageAsync(
                    (int)FileTransferMessageType.FileChunkMsg,
                    new FileChunkMessage
                    {
                        ChunkIndex = chunkIndex++,
                        Data       = ByteString.CopyFrom(buffer, 0, bytesRead),
                    },
                    useCompression: true);
            }

            var hashBytes = hasher.GetHashAndReset();
            var checksum = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

            await context.SendMessageAsync(
                (int)FileTransferMessageType.FileTransferDoneMsg,
                new FileTransferCompleteMessage
                {
                    TotalChunks    = chunkIndex,
                    Sha256Checksum = checksum,
                });

            _logger.LogInformation("FileDownload: '{FileId}' complete ({Chunks} chunks, SHA-256 {Checksum})",
                request.FileId, chunkIndex, checksum);
        }

        return (null, 0); // response already written to wire
    }
}
