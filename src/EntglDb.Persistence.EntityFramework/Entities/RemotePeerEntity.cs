using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntglDb.Persistence.EntityFramework.Entities;

/// <summary>
/// Entity Framework entity representing a remote peer configuration.
/// </summary>
[Table("RemotePeers")]
public class RemotePeerEntity
{
    /// <summary>
    /// Gets or sets the unique identifier for the remote peer node.
    /// </summary>
    [Key]
    [MaxLength(256)]
    public string NodeId { get; set; } = "";

    /// <summary>
    /// Gets or sets the network address of the remote peer (hostname:port).
    /// </summary>
    [Required]
    [MaxLength(512)]
    public string Address { get; set; } = "";

    /// <summary>
    /// Gets or sets the type of the peer (0=LanDiscovered, 1=StaticRemote, 2=CloudRemote).
    /// </summary>
    public int Type { get; set; }

    /// <summary>
    /// Gets or sets the OAuth2 configuration as JSON string (for CloudRemote type).
    /// </summary>
    public string? OAuth2Json { get; set; }

    /// <summary>
    /// Gets or sets whether this peer is enabled for synchronization.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the collection interests as a JSON string.
    /// </summary>
    public string? InterestsJson { get; set; }
}
