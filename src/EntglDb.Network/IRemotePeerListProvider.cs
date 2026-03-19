using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core.Network;

namespace EntglDb.Network;

/// <summary>
/// Provides access to the list of persisted remote peer configurations.
/// Used by CompositeDiscoveryService to merge remote peers with LAN-discovered peers
/// without depending on EntglDb.Core.Storage.
/// </summary>
public interface IRemotePeerListProvider
{
    Task<IEnumerable<RemotePeerConfiguration>> GetRemotePeersAsync(CancellationToken cancellationToken = default);
}
