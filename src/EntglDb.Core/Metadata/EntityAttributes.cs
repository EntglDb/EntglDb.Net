using System;

namespace EntglDb.Core.Metadata;

/// <summary>
/// Marks a property as the primary key for an entity.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class PrimaryKeyAttribute : Attribute
{
    /// <summary>
    /// Gets or sets whether the key should be auto-generated if empty.
    /// Default is true.
    /// </summary>
    public bool AutoGenerate { get; set; } = true;
}

/// <summary>
/// Marks a property as indexed for improved query performance.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class IndexedAttribute : Attribute
{
    /// <summary>
    /// Gets or sets whether this index should enforce uniqueness.
    /// </summary>
    public bool Unique { get; set; } = false;
}
