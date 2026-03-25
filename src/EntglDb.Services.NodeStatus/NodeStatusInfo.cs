using System;
using System.Collections.Generic;

namespace EntglDb.Services.NodeStatus;

/// <summary>
/// Runtime status snapshot reported by a remote peer.
/// </summary>
public sealed class NodeStatusInfo
{
    /// <summary>The unique node identifier of the responding peer.</summary>
    public string NodeId { get; init; } = string.Empty;

    /// <summary>How long the peer process has been running.</summary>
    public TimeSpan Uptime { get; init; }

    /// <summary>Addresses (<c>host:port</c>) of all peers currently visible to the responding node.</summary>
    public IReadOnlyList<string> KnownPeerAddresses { get; init; } = Array.Empty<string>();
}
