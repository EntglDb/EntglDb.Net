using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EntglDb.Persistence.EntityFramework.Entities;

/// <summary>
/// Entity Framework entity representing a document in the database.
/// </summary>
[Table("Documents")]
public class DocumentEntity
{
    /// <summary>
    /// Gets or sets the collection name.
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string Collection { get; set; } = "";

    /// <summary>
    /// Gets or sets the document key.
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string Key { get; set; } = "";

    /// <summary>
    /// Gets or sets the JSON content as string.
    /// </summary>
    [Required]
    public string ContentJson { get; set; } = "";

    /// <summary>
    /// Gets or sets the physical time component of the HLC timestamp.
    /// </summary>
    public long UpdatedAtPhysicalTime { get; set; }

    /// <summary>
    /// Gets or sets the logical counter component of the HLC timestamp.
    /// </summary>
    public int UpdatedAtLogicalCounter { get; set; }

    /// <summary>
    /// Gets or sets the node ID component of the HLC timestamp.
    /// </summary>
    [Required]
    [MaxLength(256)]
    public string UpdatedAtNodeId { get; set; } = "";

    /// <summary>
    /// Gets or sets whether the document is deleted.
    /// </summary>
    public bool IsDeleted { get; set; }
}
