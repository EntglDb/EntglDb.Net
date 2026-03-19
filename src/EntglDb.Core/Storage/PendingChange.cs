using EntglDb.Core;

namespace EntglDb.Core.Storage;

/// <summary>
/// Represents a pending local change waiting to be flushed to the oplog.
/// </summary>
public class PendingChange
{
    /// <summary>
    /// Gets the collection name.
    /// </summary>
    public string Collection { get; }

    /// <summary>
    /// Gets the document key within the collection.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the type of operation (Put or Delete).
    /// </summary>
    public OperationType OperationType { get; }

    /// <summary>
    /// Gets the HLC timestamp when the change was detected locally.
    /// </summary>
    public HlcTimestamp Timestamp { get; }

    /// <summary>
    /// Initializes a new instance of the PendingChange class.
    /// </summary>
    public PendingChange(string collection, string key, OperationType operationType, HlcTimestamp timestamp)
    {
        Collection = collection;
        Key = key;
        OperationType = operationType;
        Timestamp = timestamp;
    }
}
