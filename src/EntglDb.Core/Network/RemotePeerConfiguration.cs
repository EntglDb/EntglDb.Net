using EntglDb.Core.Metadata;

namespace EntglDb.Core.Network;

/// <summary>
/// Configuration for a remote peer node that is persistent across restarts.
/// This collection is automatically synchronized across all nodes in the cluster.
/// </summary>
public class RemotePeerConfiguration
{
    /// <summary>
    /// Gets or sets the unique identifier for the remote peer node.
    /// </summary>
    [PrimaryKey(AutoGenerate = false)]
    public string NodeId { get; set; } = "";
    
    /// <summary>
    /// Gets or sets the network address of the remote peer (hostname:port).
    /// </summary>
    public string Address { get; set; } = "";
    
    /// <summary>
    /// Gets or sets the type of the peer (StaticRemote or CloudRemote).
    /// </summary>
    public PeerType Type { get; set; }
    
    /// <summary>
    /// Gets or sets the OAuth2 configuration as JSON string (required for CloudRemote type).
    /// Contains authority, clientId, clientSecret, and scopes.
    /// </summary>
    public string? OAuth2Json { get; set; }
    
    /// <summary>
    /// Gets or sets whether this peer is enabled for synchronization.
    /// Disabled peers are stored but not used for sync.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
