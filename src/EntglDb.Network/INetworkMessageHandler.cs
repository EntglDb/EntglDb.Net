using System.Threading.Tasks;
using Google.Protobuf;

namespace EntglDb.Network;

/// <summary>
/// Defines a network message handler for the EntglDb sync server.
/// Both built-in core handlers and user-supplied custom handlers implement this interface.
/// All implementations are registered in the DI container and are collected by
/// <see cref="TcpSyncServer"/> at construction to build the handler dispatch registry.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="MessageType"/> property returns a raw <c>int</c> wire-type identifier so that
/// handlers in any assembly can register for any message range without depending on a shared
/// protobuf enum.  Built-in sync handlers use values 3–15 (see <c>SyncMessageType</c> in
/// <c>EntglDb.Sync</c>).  Custom service handlers should use values 32+ to avoid collisions.
/// </para>
/// <para>
/// Register user-defined handlers <em>after</em> calling <c>AddEntglDbNetwork</c> so that
/// they appear last in the DI collection and can override core handlers for the same
/// <see cref="MessageType"/>:
/// </para>
/// <code>
/// services.AddEntglDbNetwork&lt;MyConfigProvider&gt;();
/// services.AddSingleton&lt;INetworkMessageHandler, MyCustomHandler&gt;(); // added after → takes precedence
/// </code>
/// <para>
/// Return <c>(null, 0)</c> when the handler streams its response directly
/// via <see cref="IMessageHandlerContext.SendMessageAsync"/> (i.e. no further response needs
/// to be sent by the dispatcher).
/// </para>
/// </remarks>
public interface INetworkMessageHandler
{
    /// <summary>
    /// The raw wire message-type integer this handler is responsible for processing.
    /// </summary>
    int MessageType { get; }

    /// <summary>
    /// Handles an incoming message and returns an optional response.
    /// </summary>
    /// <param name="context">Context containing the raw payload, remote endpoint, and cancellation token.</param>
    /// <returns>
    /// A tuple of the response message and its raw wire message-type integer.
    /// Return <c>(null, 0)</c> if the handler sends the response itself (streaming).
    /// </returns>
    Task<(IMessage? Response, int ResponseType)> HandleAsync(IMessageHandlerContext context);
}
