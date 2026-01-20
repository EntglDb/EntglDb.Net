using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Network.Security;

/// <summary>
/// Provides a no-operation implementation of the peer handshake service that performs no handshake and always returns
/// null.
/// </summary>
/// <remarks>This class can be used in scenarios where a handshake is not required or for testing purposes. All
/// handshake attempts using this service will result in no cipher state being established.</remarks>
public class NoOpHandshakeService : IPeerHandshakeService
{
    /// <summary>
    /// Performs a handshake over the specified stream to establish a secure communication channel between two nodes
    /// asynchronously.
    /// </summary>
    /// <param name="stream">The stream used for exchanging handshake messages between nodes. Must be readable and writable.</param>
    /// <param name="isInitiator">true to initiate the handshake as the local node; otherwise, false to respond as the remote node.</param>
    /// <param name="myNodeId">The unique identifier of the local node participating in the handshake. Cannot be null.</param>
    /// <param name="token">A cancellation token that can be used to cancel the handshake operation.</param>
    /// <returns>A task that represents the asynchronous handshake operation. The task result contains a CipherState if the
    /// handshake succeeds; otherwise, null.</returns>
    public Task<CipherState?> HandshakeAsync(Stream stream, bool isInitiator, string myNodeId, CancellationToken token)
    {
        return Task.FromResult<CipherState?>(null);
    }
}
