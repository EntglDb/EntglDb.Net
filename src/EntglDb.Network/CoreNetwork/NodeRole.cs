namespace EntglDb.Core.Network;

/// <summary>
/// Defines the role of a node in the distributed network cluster.
/// </summary>
public enum NodeRole
{
    /// <summary>
    /// Standard member node that synchronizes only within the local area network.
    /// Does not connect to cloud remote nodes.
    /// </summary>
    Member = 0,
    
    /// <summary>
    /// Leader node that acts as a gateway to cloud remote nodes.
    /// Elected via the Bully algorithm (lexicographically smallest NodeId).
    /// Responsible for synchronizing local cluster changes with cloud nodes.
    /// </summary>
    CloudGateway = 1
}
