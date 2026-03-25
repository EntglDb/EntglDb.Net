using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Services.FileTransfer;

/// <summary>
/// Server-side abstraction that controls which files are exposed to remote peers.
/// </summary>
/// <remarks>
/// <para>
/// Implement this interface and register it with DI to make files downloadable.
/// File identifiers (<c>fileId</c>) are opaque strings defined by the provider —
/// they are never treated as file-system paths by the framework, preventing
/// directory-traversal attacks.
/// </para>
/// <para>
/// Example: expose a read-only catalogue of report files identified by a GUID key.
/// </para>
/// </remarks>
public interface IFileProvider
{
    /// <summary>
    /// Returns metadata for the given <paramref name="fileId"/>, or <c>null</c> if the file is not found.
    /// </summary>
    Task<FileTransferInfo?> GetInfoAsync(string fileId, CancellationToken ct = default);

    /// <summary>
    /// Opens a readable stream for the given <paramref name="fileId"/>, or <c>null</c> if not found.
    /// The caller is responsible for disposing the returned stream.
    /// </summary>
    Task<Stream?> OpenReadAsync(string fileId, CancellationToken ct = default);
}
