using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

namespace EntglDb.Network;

/// <summary>
/// Default implementation of <see cref="IPeerMessenger"/>.
/// Delegates connection management to the injected <see cref="IPeerConnectionPool{TcpPeerClient}"/>.
/// </summary>
internal sealed class PeerMessenger : IPeerMessenger
{
    private readonly IPeerConnectionPool _pool;

    public PeerMessenger(IPeerConnectionPool pool)
    {
        _pool = pool;
    }

    /// <inheritdoc/>
    public async Task<(int ResponseType, byte[] Payload)> SendAndReceiveAsync(
        string peerAddress, int messageType, IMessage message, CancellationToken token = default)
    {
        // Held for connect+handshake (if needed) through the response read - see
        // IPeerConnectionPool.AcquireAsync remarks: without this, a concurrent caller sharing this
        // peer's pooled connection (e.g. the sync orchestrator's own exchange) could read the response
        // meant for this call, or vice versa.
        await using var lease = await _pool.AcquireAsync(peerAddress, token);
        var client = await _pool.GetOrConnectAsync(peerAddress, token: token);
        await client.SendCustomAsync(messageType, message, token);
        return await client.ReceiveAsync(token);
    }

    /// <inheritdoc/>
    public async Task SendAsync(
        string peerAddress, int messageType, IMessage message, CancellationToken token = default)
    {
        await using var lease = await _pool.AcquireAsync(peerAddress, token);
        var client = await _pool.GetOrConnectAsync(peerAddress, token: token);
        await client.SendCustomAsync(messageType, message, token);
    }
}
