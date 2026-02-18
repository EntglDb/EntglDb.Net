using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntglDb.Persistence.EntityFramework.Entities;

/// <summary>
/// EF Core entity representing document metadata for sync tracking.
/// Stores HLC timestamp and deleted state for each document without modifying application entities.
/// </summary>
[Table("DocumentMetadata")]
public class DocumentMetadataEntity
{
    /// <summary>
    /// Gets or sets the unique identifier (auto-generated).
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>
    /// Gets or sets the collection name (business key part 1).
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string Collection { get; set; } = "";

    /// <summary>
    /// Gets or sets the document key within the collection (business key part 2).
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string Key { get; set; } = "";

    /// <summary>
    /// Gets or sets the physical time component of the HLC timestamp.
    /// </summary>
    public long HlcPhysicalTime { get; set; }

    /// <summary>
    /// Gets or sets the logical counter component of the HLC timestamp.
    /// </summary>
    public int HlcLogicalCounter { get; set; }

    /// <summary>
    /// Gets or sets the node ID that last modified this document.
    /// </summary>
    [MaxLength(256)]
    public string HlcNodeId { get; set; } = "";

    /// <summary>
    /// Gets or sets whether this document is marked as deleted (tombstone).
    /// </summary>
    public bool IsDeleted { get; set; }
}
