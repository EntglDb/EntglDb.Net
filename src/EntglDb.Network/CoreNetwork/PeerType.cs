namespace EntglDb.Core.Network;

/// <summary>
/// Defines the type of peer node in the distributed network.
/// </summary>
public enum PeerType
{
    /// <summary>
    /// Peer discovered via UDP broadcast on the local area network.
    /// These peers are ephemeral and removed after timeout when no longer broadcasting.
    /// </summary>
    LanDiscovered = 0,
    
    /// <summary>
    /// Peer manually configured with a static address.
    /// These peers are persistent across restarts and stored in the database.
    /// </summary>
    StaticRemote = 1,
    
    /// <summary>
    /// Cloud remote node with OAuth2 authentication.
    /// Always active if internet connectivity is available.
    /// Synchronized only by the elected leader node to reduce overhead.
    /// </summary>
    CloudRemote = 2
}
