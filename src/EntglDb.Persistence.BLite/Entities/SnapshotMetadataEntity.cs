using System.ComponentModel.DataAnnotations;

namespace EntglDb.Persistence.BLite.Entities;

/// <summary>
/// BLite entity representing snapshot metadata (oplog pruning checkpoint).
/// </summary>
public class SnapshotMetadataEntity
{
    /// <summary>
    /// Gets or sets the unique identifier for this entity (technical key).
    /// Auto-generated GUID string.
    /// </summary>
    [Key]
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the node identifier (business key).
    /// </summary>
    public string NodeId { get; set; } = "";

    /// <summary>
    /// Gets or sets the physical time component of the timestamp.
    /// </summary>
    public long TimestampPhysicalTime { get; set; }

    /// <summary>
    /// Gets or sets the logical counter component of the timestamp.
    /// </summary>
    public int TimestampLogicalCounter { get; set; }

    /// <summary>
    /// Gets or sets the hash of the snapshot.
    /// </summary>
    public string Hash { get; set; } = "";
}
