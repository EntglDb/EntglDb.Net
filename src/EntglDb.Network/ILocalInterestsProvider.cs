using System.Collections.Generic;

namespace EntglDb.Network;

/// <summary>
/// Provides the list of collections that the local node is interested in receiving data for.
/// Used by the transport layer (TcpSyncServer, UdpDiscoveryService) to advertise local interests
/// to remote peers during handshake and discovery without depending on EntglDb.Core.
/// </summary>
public interface ILocalInterestsProvider
{
    IEnumerable<string> InterestedCollection { get; }
}
