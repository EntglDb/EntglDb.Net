using System.ComponentModel.DataAnnotations;

namespace EntglDb.Persistence.BLite.Entities;

/// <summary>
/// BLite entity representing document metadata for sync tracking.
/// Stores HLC timestamp and deleted state for each document without modifying application entities.
/// </summary>
public class DocumentMetadataEntity
{
    /// <summary>
    /// Gets or sets the unique identifier for this entity (technical key).
    /// Auto-generated GUID string.
    /// </summary>
    [Key]
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the collection name (business key part 1).
    /// </summary>
    public string Collection { get; set; } = "";

    /// <summary>
    /// Gets or sets the document key within the collection (business key part 2).
    /// </summary>
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
    public string HlcNodeId { get; set; } = "";

    /// <summary>
    /// Gets or sets whether this document is marked as deleted (tombstone).
    /// </summary>
    public bool IsDeleted { get; set; }
}
