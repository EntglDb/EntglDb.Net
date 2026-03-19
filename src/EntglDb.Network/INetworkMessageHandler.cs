using System.Threading.Tasks;
using EntglDb.Network.Proto;
using Google.Protobuf;

namespace EntglDb.Network;

/// <summary>
/// Defines a user-supplied handler for a custom (or overridden) network message type.
/// Implementations can be registered in the DI container and will be merged into the
/// server's handler registry at startup, alongside the built-in core handlers.
/// </summary>
/// <remarks>
/// <para>
/// If a user handler targets the same <see cref="MessageType"/> as a built-in core handler,
/// the user handler takes precedence and the core handler is replaced (a warning is logged).
/// </para>
/// <para>
/// Return <c>(null, MessageType.Unknown)</c> when the handler streams its response directly
/// (i.e. no further response needs to be sent by the dispatcher).
/// </para>
/// </remarks>
public interface INetworkMessageHandler
{
    /// <summary>
    /// The <see cref="MessageType"/> this handler is responsible for processing.
    /// </summary>
    MessageType MessageType { get; }

    /// <summary>
    /// Handles an incoming message and returns an optional response.
    /// </summary>
    /// <param name="context">Context containing the raw payload, remote endpoint, and cancellation token.</param>
    /// <returns>
    /// A tuple of the response message and its <see cref="MessageType"/>.
    /// Return <c>(null, MessageType.Unknown)</c> if the handler sends the response itself (streaming).
    /// </returns>
    Task<(IMessage? Response, MessageType ResponseType)> HandleAsync(IMessageHandlerContext context);
}
