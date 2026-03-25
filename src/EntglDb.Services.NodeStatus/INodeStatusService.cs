using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Services.NodeStatus;

/// <summary>
/// Client-side API for querying the runtime status of a remote peer
/// over the EntglDb P2P mesh network.
/// </summary>
/// <remarks>
/// Inject this service and call <see cref="QueryAsync"/> with the peer's
/// <c>host:port</c> address.  The underlying TCP connection is shared with
/// the sync engine — no extra socket is opened.
/// <code>
/// var status = await nodeStatusService.QueryAsync("192.168.1.10:7000", ct);
/// Console.WriteLine($"{status.NodeId} up for {status.Uptime.TotalMinutes:F0} min");
/// </code>
/// </remarks>
public interface INodeStatusService
{
    /// <summary>
    /// Queries a remote peer for its current runtime status.
    /// </summary>
    /// <param name="peerAddress">Remote peer address in <c>host:port</c> format.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="NodeStatusInfo"/> snapshot from the remote peer.</returns>
    Task<NodeStatusInfo> QueryAsync(string peerAddress, CancellationToken ct = default);
}
