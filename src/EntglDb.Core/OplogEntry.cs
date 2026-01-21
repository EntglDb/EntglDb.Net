using System.Text.Json;

namespace EntglDb.Core;

public enum OperationType
{
    Put,
    Delete
}

public class OplogEntry
{
    public string Collection { get; }
    public string Key { get; }
    public OperationType Operation { get; }
    public JsonElement? Payload { get; }
    public HlcTimestamp Timestamp { get; }
    
    /// <summary>
    /// Sequential operation number per node for gap detection.
    /// Used to identify missing operations in synchronization.
    /// </summary>
    public long SequenceNumber { get; }

    public OplogEntry(string collection, string key, OperationType operation, JsonElement? payload, HlcTimestamp timestamp, long sequenceNumber = 0)
    {
        Collection = collection;
        Key = key;
        Operation = operation;
        Payload = payload;
        Timestamp = timestamp;
        SequenceNumber = sequenceNumber;
    }
}
