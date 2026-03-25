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
        var client = await _pool.GetOrConnectAsync(peerAddress, token: token);
        await client.SendCustomAsync(messageType, message, token);
        return await client.ReceiveAsync(token);
    }

    /// <inheritdoc/>
    public async Task SendAsync(
        string peerAddress, int messageType, IMessage message, CancellationToken token = default)
    {
        var client = await _pool.GetOrConnectAsync(peerAddress, token: token);
        await client.SendCustomAsync(messageType, message, token);
    }
}
