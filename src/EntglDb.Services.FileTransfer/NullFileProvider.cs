using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Services.FileTransfer;

/// <summary>
/// Default <see cref="IFileProvider"/> registered by <see cref="FileTransferExtensions.AddEntglDbFileTransfer"/> -
/// reports every file as not found. Lets a node that only ever downloads files (never serves any) use
/// <c>AddEntglDbFileTransfer()</c> without also having to implement and register an <see cref="IFileProvider"/>
/// of its own just to satisfy <see cref="FileQueryHandler"/>/<see cref="FileDownloadHandler"/>'s constructor
/// dependency. A node that does want to serve files registers its own provider after this call; DI resolves
/// a single (non-enumerable) service to the last registration, so the real provider wins.
/// </summary>
public sealed class NullFileProvider : IFileProvider
{
    public Task<FileTransferInfo?> GetInfoAsync(string fileId, CancellationToken ct = default) =>
        Task.FromResult<FileTransferInfo?>(null);

    public Task<Stream?> OpenReadAsync(string fileId, CancellationToken ct = default) =>
        Task.FromResult<Stream?>(null);
}
