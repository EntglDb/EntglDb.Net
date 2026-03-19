using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

namespace EntglDb.Network;

/// <summary>
/// Context passed to a <see cref="INetworkMessageHandler"/> when a message is received from a remote peer.
/// </summary>
public interface IMessageHandlerContext
{
    /// <summary>
    /// The raw protobuf payload bytes of the incoming message.
    /// Use the corresponding protobuf parser (e.g. <c>MyRequest.Parser.ParseFrom(Payload)</c>) to deserialize.
    /// </summary>
    byte[] Payload { get; }

    /// <summary>
    /// The remote endpoint of the connected client.
    /// </summary>
    EndPoint? RemoteEndPoint { get; }

    /// <summary>
    /// Cancellation token for the operation.
    /// </summary>
    CancellationToken CancellationToken { get; }

    /// <summary>
    /// Sends a message directly to the connected client on the current stream.
    /// Use this for streaming responses (e.g. multi-chunk transfers) where the handler
    /// writes its own response rather than returning a single <see cref="IMessage"/>.
    /// </summary>
    /// <param name="type">The raw wire message-type integer of the outgoing message.</param>
    /// <param name="message">The protobuf message to send.</param>
    /// <param name="useCompression">Whether to compress the message payload. Defaults to <c>false</c>.</param>
    Task SendMessageAsync(int type, IMessage message, bool useCompression = false);
}
