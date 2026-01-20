using System;
using System.Threading.Tasks;

namespace EntglDb.Network.Leadership;

/// <summary>
/// Event arguments for leadership change events.
/// </summary>
public class LeadershipChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the NodeId of the current cloud gateway (leader).
    /// Null if no leader is elected.
    /// </summary>
    public string? CurrentGatewayNodeId { get; }

    /// <summary>
    /// Gets whether the local node is now the cloud gateway.
    /// </summary>
    public bool IsLocalNodeGateway { get; }

    /// <summary>
    /// Initializes a new instance of the LeadershipChangedEventArgs class.
    /// </summary>
    public LeadershipChangedEventArgs(string? currentGatewayNodeId, bool isLocalNodeGateway)
    {
        CurrentGatewayNodeId = currentGatewayNodeId;
        IsLocalNodeGateway = isLocalNodeGateway;
    }
}

/// <summary>
/// Service for managing leader election in a distributed cluster.
/// Uses the Bully algorithm where the node with the lexicographically smallest NodeId becomes the leader.
/// Only the leader (Cloud Gateway) synchronizes with remote cloud nodes.
/// </summary>
public interface ILeaderElectionService
{
    /// <summary>
    /// Gets whether the local node is currently the cloud gateway (leader).
    /// </summary>
    bool IsCloudGateway { get; }

    /// <summary>
    /// Gets the NodeId of the current cloud gateway, or null if no gateway is elected.
    /// </summary>
    string? CurrentGatewayNodeId { get; }

    /// <summary>
    /// Event raised when leadership changes.
    /// </summary>
    event EventHandler<LeadershipChangedEventArgs>? LeadershipChanged;

    /// <summary>
    /// Starts the leader election service.
    /// </summary>
    Task Start();

    /// <summary>
    /// Stops the leader election service.
    /// </summary>
    Task Stop();
}
