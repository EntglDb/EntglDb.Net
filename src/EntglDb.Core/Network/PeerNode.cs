using System;

namespace EntglDb.Core.Network;

/// <summary>
/// Represents a peer node in a distributed network, including its unique identifier, network address, and last seen
/// timestamp.
/// </summary>
public class PeerNode
{
    /// <summary>
    /// Gets the unique identifier for the node.
    /// </summary>
    public string NodeId { get; }

    /// <summary>
    /// Gets the address associated with the current instance.
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// Gets the date and time when the entity was last observed or updated.
    /// </summary>
    public DateTimeOffset LastSeen { get; }

    /// <summary>
    /// Gets the configuration settings for the peer node.
    /// </summary>
    public PeerNodeConfiguration Configuration { get; }

    /// <summary>
    /// Gets the type of the peer node (LanDiscovered, StaticRemote, or CloudRemote).
    /// </summary>
    public PeerType Type { get; }

    /// <summary>
    /// Gets the role of the peer node in the cluster (Member or CloudGateway).
    /// </summary>
    public NodeRole Role { get; }

    /// <summary>
    /// Gets whether this peer is persistent (stored in database) or ephemeral (LAN discovery).
    /// </summary>
    public bool IsPersistent => Type != PeerType.LanDiscovered;

    /// <summary>
    /// Initializes a new instance of the PeerNode class with the specified node identifier, network address, and last
    /// seen timestamp.
    /// </summary>
    /// <param name="nodeId">The unique identifier for the peer node. Cannot be null or empty.</param>
    /// <param name="address">The network address of the peer node. Cannot be null or empty.</param>
    /// <param name="lastSeen">The date and time when the peer node was last seen, expressed as a DateTimeOffset.</param>
    /// <param name="type">The type of the peer node. Defaults to LanDiscovered.</param>
    /// <param name="role">The role of the peer node. Defaults to Member.</param>
    public PeerNode(string nodeId, string address, DateTimeOffset lastSeen, PeerType type = PeerType.LanDiscovered, NodeRole role = NodeRole.Member)
    {
        NodeId = nodeId;
        Address = address;
        LastSeen = lastSeen;
        Type = type;
        Role = role;
    }
}
