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

    public OplogEntry(string collection, string key, OperationType operation, JsonElement? payload, HlcTimestamp timestamp)
    {
        Collection = collection;
        Key = key;
        Operation = operation;
        Payload = payload;
        Timestamp = timestamp;
    }
}
