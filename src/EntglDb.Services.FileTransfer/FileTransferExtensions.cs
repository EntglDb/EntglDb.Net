using EntglDb.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EntglDb.Services.FileTransfer;

/// <summary>
/// Extension methods for registering EntglDb file transfer services.
/// </summary>
public static class FileTransferExtensions
{
    /// <summary>
    /// Adds the EntglDb file transfer service to the DI container.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registers:
    /// <list type="bullet">
    ///   <item><see cref="FileQueryHandler"/> as <see cref="INetworkMessageHandler"/> (server-side, wire type 1100)</item>
    ///   <item><see cref="FileDownloadHandler"/> as <see cref="INetworkMessageHandler"/> (server-side streaming, wire type 1102)</item>
    ///   <item><see cref="IFileTransferService"/> / <see cref="FileTransferClient"/> (client-side download API)</item>
    ///   <item><see cref="IFileProvider"/> / <see cref="NullFileProvider"/> (server-side default - see below)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Call <c>AddEntglDbNetwork&lt;T&gt;()</c> before this method so that
    /// <see cref="IPeerMessenger"/> and <see cref="IPeerConnectionPool"/> are already registered.
    /// </para>
    /// <para>
    /// A node that only ever downloads files (never serves any) needs nothing further - it uses the
    /// registered <see cref="NullFileProvider"/> default, reporting every file as not found. To make files
    /// available for remote download instead, register a real provider <em>after</em> this call:
    /// <code>
    /// services.AddEntglDbFileTransfer();
    /// services.AddSingleton&lt;IFileProvider, MyFileProvider&gt;();
    /// </code>
    /// <see cref="FileQueryHandler"/>/<see cref="FileDownloadHandler"/> take a single (non-enumerable)
    /// <see cref="IFileProvider"/>, so the last registration wins - the call above's own provider overrides
    /// the default even though both end up registered.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddEntglDbFileTransfer(this IServiceCollection services)
    {
        // Server side — file query (single response)
        services.AddSingleton<INetworkMessageHandler, FileQueryHandler>();

        // Server side — file download (streaming response)
        services.AddSingleton<INetworkMessageHandler, FileDownloadHandler>();

        // Client side
        services.TryAddSingleton<IFileTransferService, FileTransferClient>();

        // Server-side default so a download-only consumer doesn't have to implement IFileProvider itself
        // just to satisfy FileQueryHandler/FileDownloadHandler's constructor dependency - see remarks
        // above. Must be TryAddSingleton (not AddSingleton) so a real provider the caller registers
        // afterward via plain AddSingleton becomes the winning "last registration" for the single-instance
        // resolution FileQueryHandler/FileDownloadHandler actually use.
        services.TryAddSingleton<IFileProvider, NullFileProvider>();

        return services;
    }
}
