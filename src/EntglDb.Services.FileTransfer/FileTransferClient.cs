using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Network;
using EntglDb.Services.FileTransfer.Proto;
using Microsoft.Extensions.Logging;

namespace EntglDb.Services.FileTransfer;

/// <summary>
/// Default implementation of <see cref="IFileTransferService"/>.
/// Uses <see cref="IPeerMessenger"/> for single-response queries and
/// <see cref="IPeerConnectionPool"/> directly for streaming downloads.
/// </summary>
internal sealed class FileTransferClient : IFileTransferService
{
    private readonly IPeerMessenger _messenger;
    private readonly IPeerConnectionPool _pool;
    private readonly ILogger<FileTransferClient> _logger;

    public FileTransferClient(
        IPeerMessenger messenger,
        IPeerConnectionPool pool,
        ILogger<FileTransferClient> logger)
    {
        _messenger = messenger;
        _pool = pool;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<FileTransferInfo?> QueryAsync(
        string peerAddress, string fileId, CancellationToken ct = default)
    {
        var (responseType, payload) = await _messenger.SendAndReceiveAsync(
            peerAddress,
            (int)FileTransferMessageType.FileQueryReq,
            new FileQueryRequest { FileId = fileId },
            ct);

        if (responseType != (int)FileTransferMessageType.FileQueryRes)
            throw new InvalidOperationException(
                $"Unexpected response type {responseType} from peer '{peerAddress}'.");

        var response = FileQueryResponse.Parser.ParseFrom(payload);
        if (!response.Found)
            return null;

        return new FileTransferInfo
        {
            FileId          = response.FileId,
            FileName        = response.FileName,
            SizeBytes       = response.SizeBytes,
            Sha256Checksum  = string.IsNullOrEmpty(response.Sha256Checksum) ? null : response.Sha256Checksum,
        };
    }

    /// <inheritdoc/>
    public async Task DownloadAsync(
        string peerAddress, string fileId, Stream destination, CancellationToken ct = default)
    {
        // Obtain (or reuse) the shared TCP connection for this peer.
        var client = await _pool.GetOrConnectAsync(peerAddress, token: ct);

        _logger.LogInformation("FileDownload: requesting '{FileId}' from {Peer}", fileId, peerAddress);

        await client.SendCustomAsync(
            (int)FileTransferMessageType.FileDownloadReq,
            new FileDownloadRequest { FileId = fileId },
            ct);

        // Receive chunks until FileTransferDoneMsg.
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int chunksReceived = 0;

        while (true)
        {
            var (msgType, payload) = await client.ReceiveAsync(ct);

            if (msgType == (int)FileTransferMessageType.FileChunkMsg)
            {
                var chunk = FileChunkMessage.Parser.ParseFrom(payload);
                var data = chunk.Data.ToByteArray();
                hasher.AppendData(data);
                await destination.WriteAsync(data, 0, data.Length, ct);
                chunksReceived++;
            }
            else if (msgType == (int)FileTransferMessageType.FileTransferDoneMsg)
            {
                var done = FileTransferCompleteMessage.Parser.ParseFrom(payload);

                if (done.TotalChunks < 0)
                    throw new InvalidOperationException(
                        $"Remote peer reported file '{fileId}' not found.");

                var hashBytes = hasher.GetHashAndReset();
                var actualChecksum = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                if (!string.Equals(actualChecksum, done.Sha256Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    _pool.Invalidate(peerAddress); // evict potentially dirty connection
                    throw new FileTransferIntegrityException(fileId, done.Sha256Checksum, actualChecksum);
                }

                _logger.LogInformation(
                    "FileDownload: '{FileId}' complete ({Chunks} chunks, SHA-256 verified)", fileId, chunksReceived);
                return;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unexpected message type {msgType} during file download of '{fileId}'.");
            }
        }
    }
}
