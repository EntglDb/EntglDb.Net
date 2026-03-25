using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Services.FileTransfer;

/// <summary>
/// Client-side API for querying and downloading files from remote peers
/// over the EntglDb P2P mesh network.
/// </summary>
/// <remarks>
/// File data is transferred as a sequence of binary chunks, with SHA-256
/// integrity verification performed automatically on receipt.
/// The underlying TCP connection is shared with the sync engine.
/// <code>
/// // Query before downloading
/// var info = await fileService.QueryAsync("192.168.1.10:7000", "report-2026-q1");
/// if (info != null)
/// {
///     using var fs = File.OpenWrite("report.pdf");
///     await fileService.DownloadAsync("192.168.1.10:7000", "report-2026-q1", fs, ct);
/// }
/// </code>
/// </remarks>
public interface IFileTransferService
{
    /// <summary>
    /// Queries a remote peer for metadata about a specific file.
    /// Returns <c>null</c> if the file is not available on that peer.
    /// </summary>
    /// <param name="peerAddress">Remote peer address in <c>host:port</c> format.</param>
    /// <param name="fileId">Opaque file identifier defined by the remote peer's <see cref="IFileProvider"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<FileTransferInfo?> QueryAsync(string peerAddress, string fileId, CancellationToken ct = default);

    /// <summary>
    /// Downloads a file from a remote peer and writes it to <paramref name="destination"/>.
    /// Throws <see cref="FileTransferIntegrityException"/> if the SHA-256 checksum does not match.
    /// </summary>
    /// <param name="peerAddress">Remote peer address in <c>host:port</c> format.</param>
    /// <param name="fileId">Opaque file identifier.</param>
    /// <param name="destination">Writable stream to receive the file data.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DownloadAsync(string peerAddress, string fileId, Stream destination, CancellationToken ct = default);
}
