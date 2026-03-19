using System.ComponentModel.DataAnnotations;

namespace EntglDb.Persistence.Entities;

/// <summary>
/// Entity representing a pending local change waiting to be flushed to the oplog.
/// Uses upsert semantics: Id = "collection/key", only the last operation per document survives.
/// </summary>
public class PendingChangeEntity
{
    /// <summary>
    /// Gets or sets the unique identifier for this pending change (business key).
    /// Format: "{collection}/{key}" to ensure one entry per document.
    /// </summary>
    [Key]
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the collection name.
    /// </summary>
    public string Collection { get; set; } = "";

    /// <summary>
    /// Gets or sets the document key within the collection.
    /// </summary>
    public string Key { get; set; } = "";

    /// <summary>
    /// Gets or sets the operation type (0 = Put, 1 = Delete).
    /// </summary>
    public int OperationType { get; set; }

    /// <summary>
    /// Gets or sets the HLC timestamp physical time when this change was detected locally.
    /// Used when flushing to oplog to preserve the original timestamp.
    /// </summary>
    public long HlcPhysicalTime { get; set; }

    /// <summary>
    /// Gets or sets the HLC timestamp logical counter.
    /// </summary>
    public int HlcLogicalCounter { get; set; }

    /// <summary>
    /// Gets or sets the HLC timestamp node ID (local node).
    /// </summary>
    public string HlcNodeId { get; set; } = "";
}
