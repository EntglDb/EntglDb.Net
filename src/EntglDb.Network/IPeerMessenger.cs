using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

namespace EntglDb.Network;

/// <summary>
/// Provides a high-level API for sending messages to remote peers over the EntglDb p2p network.
/// Manages connections internally, performing connect and handshake automatically on first use.
/// </summary>
/// <remarks>
/// Intended for custom application-level protocols that use message types 32+ (reserved below that
/// for built-in sync messages). Register custom handlers server-side via <see cref="INetworkMessageHandler"/>
/// and use this service to initiate outbound requests from the client side.
/// <code>
/// // Inject IPeerMessenger via DI
/// var (responseType, payload) = await messenger.SendAndReceiveAsync(
///     "192.168.1.10:7000",
///     MyProto.MessageType.MyRequest,
///     new MyProto.MyRequest { ... },
///     cancellationToken);
/// var response = MyProto.MyResponse.Parser.ParseFrom(payload);
/// </code>
/// </remarks>
public interface IPeerMessenger
{
    /// <summary>
    /// Sends a message to the specified peer and returns the response message type and raw payload.
    /// Connects and performs the handshake automatically if not already done.
    /// </summary>
    /// <param name="peerAddress">The remote peer address in <c>host:port</c> format.</param>
    /// <param name="messageType">The raw wire message-type integer of the outgoing message.</param>
    /// <param name="message">The protobuf message to send.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The raw wire message-type and payload bytes of the response.</returns>
    Task<(int ResponseType, byte[] Payload)> SendAndReceiveAsync(
        string peerAddress,
        int messageType,
        IMessage message,
        CancellationToken token = default);

    /// <summary>
    /// Sends a message to the specified peer without waiting for a response (fire-and-forget).
    /// Connects and performs the handshake automatically if not already done.
    /// </summary>
    /// <param name="peerAddress">The remote peer address in <c>host:port</c> format.</param>
    /// <param name="messageType">The raw wire message-type integer of the outgoing message.</param>
    /// <param name="message">The protobuf message to send.</param>
    /// <param name="token">Cancellation token.</param>
    Task SendAsync(
        string peerAddress,
        int messageType,
        IMessage message,
        CancellationToken token = default);
}
