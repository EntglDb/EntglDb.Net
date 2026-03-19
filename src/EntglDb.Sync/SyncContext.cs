using System.Net;
using System.Threading;

namespace EntglDb.Sync;

/// <summary>
/// Carries the context for an inbound message dispatched to an <see cref="INetworkMessageHandler"/>.
/// </summary>
/// <remarks>
/// <para>
/// The raw <see cref="MessageType"/> integer allows each handler to interpret the type using its
/// own proto enum without depending on a shared enum in <c>EntglDb.Network</c>.
/// </para>
/// <para>
/// <see cref="Payload"/> contains the raw protobuf bytes of the inner message.  Parse it with
/// the appropriate generated parser, e.g.
/// <c>MyProto.MyRequest.Parser.ParseFrom(ctx.Payload)</c>.
/// </para>
/// </remarks>
/// <param name="MessageType">The raw wire message-type integer.</param>
/// <param name="Payload">Raw protobuf payload bytes of the incoming message.</param>
/// <param name="PeerId">Node identifier of the remote peer (as provided during handshake).</param>
/// <param name="RemoteEndPoint">Network endpoint of the connected peer.</param>
/// <param name="CancellationToken">Cancellation token for the operation.</param>
public record SyncContext(
    int MessageType,
    byte[] Payload,
    string PeerId,
    System.Net.EndPoint? RemoteEndPoint,
    CancellationToken CancellationToken
);
