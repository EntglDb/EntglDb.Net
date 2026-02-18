using System.ComponentModel.DataAnnotations;

namespace EntglDb.Persistence.BLite.Entities;

/// <summary>
/// BLite entity representing a remote peer configuration.
/// </summary>
public class RemotePeerEntity
{
    /// <summary>
    /// Gets or sets the unique identifier for this entity (technical key).
    /// Auto-generated GUID string.
    /// </summary>
    [Key]
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the unique identifier for the remote peer node (business key).
    /// </summary>
    public string NodeId { get; set; } = "";

    /// <summary>
    /// Gets or sets the network address of the remote peer (hostname:port).
    /// </summary>
    public string Address { get; set; } = "";

    /// <summary>
    /// Gets or sets the type of the peer (0=LanDiscovered, 1=StaticRemote, 2=CloudRemote).
    /// </summary>
    public int Type { get; set; }

    /// <summary>
    /// Gets or sets the OAuth2 configuration as JSON string (for CloudRemote type).
    /// Use empty string instead of null for BLite compatibility.
    /// </summary>
    public string OAuth2Json { get; set; } = "";

    /// <summary>
    /// Gets or sets whether this peer is enabled for synchronization.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the collection interests as a JSON string.
    /// Use empty string instead of null for BLite compatibility.
    /// </summary>
    public string InterestsJson { get; set; } = "";
}
