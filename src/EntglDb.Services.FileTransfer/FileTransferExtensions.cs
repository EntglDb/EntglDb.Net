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
    /// </list>
    /// </para>
    /// <para>
    /// Call <c>AddEntglDbNetwork&lt;T&gt;()</c> before this method so that
    /// <see cref="IPeerMessenger"/> and <see cref="IPeerConnectionPool"/> are already registered.
    /// </para>
    /// <para>
    /// To make files available for remote download, also register an <see cref="IFileProvider"/>:
    /// <code>
    /// services.AddEntglDbFileTransfer();
    /// services.AddSingleton&lt;IFileProvider, MyFileProvider&gt;();
    /// </code>
    /// Without an <see cref="IFileProvider"/> the server-side handlers are registered but will
    /// return not-found for every request.
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

        return services;
    }
}
