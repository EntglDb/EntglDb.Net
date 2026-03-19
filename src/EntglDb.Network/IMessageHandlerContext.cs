using System.Net;
using System.Threading;

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
}
