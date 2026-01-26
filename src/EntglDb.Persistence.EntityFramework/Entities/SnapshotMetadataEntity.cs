using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntglDb.Persistence.EntityFramework.Entities;

/// <summary>
/// Entity representing snapshot metadata (oplog pruning checkpoint).
/// </summary>
[Table("SnapshotMetadata")]
public class SnapshotMetadataEntity
{
    [Key]
    [MaxLength(256)]
    public string NodeId { get; set; } = "";

    public long TimestampPhysicalTime { get; set; }
    public int TimestampLogicalCounter { get; set; }

    [MaxLength(128)]
    public string Hash { get; set; } = "";
}
