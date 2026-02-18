using System.ComponentModel.DataAnnotations;

namespace EntglDb.Persistence.BLite.Entities;

/// <summary>
/// BLite entity representing an operation log entry.
/// </summary>
public class OplogEntity
{
    /// <summary>
    /// Gets or sets the unique identifier for this entity (technical key).
    /// Auto-generated GUID string.
    /// </summary>
    [Key]
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the collection name.
    /// </summary>
    public string Collection { get; set; } = "";

    /// <summary>
    /// Gets or sets the document key.
    /// </summary>
    public string Key { get; set; } = "";

    /// <summary>
    /// Gets or sets the operation type (0 = Put, 1 = Delete).
    /// </summary>
    public int Operation { get; set; }

    /// <summary>
    /// Gets or sets the payload JSON (empty string for Delete operations).
    /// </summary>
    public string PayloadJson { get; set; } = "";

    /// <summary>
    /// Gets or sets the physical time component of the HLC timestamp.
    /// </summary>
    public long TimestampPhysicalTime { get; set; }

    /// <summary>
    /// Gets or sets the logical counter component of the HLC timestamp.
    /// </summary>
    public int TimestampLogicalCounter { get; set; }

    /// <summary>
    /// Gets or sets the node ID component of the HLC timestamp.
    /// </summary>
    public string TimestampNodeId { get; set; } = "";

    /// <summary>
    /// Gets or sets the cryptographic hash of this entry (business key).
    /// </summary>
    public string Hash { get; set; } = "";

    /// <summary>
    /// Gets or sets the hash of the previous entry in the chain.
    /// </summary>
    public string PreviousHash { get; set; } = "";
}
