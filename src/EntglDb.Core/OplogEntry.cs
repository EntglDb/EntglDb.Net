using System;
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
    public string Hash { get; }
    public string PreviousHash { get; }

    public OplogEntry(string collection, string key, OperationType operation, JsonElement? payload, HlcTimestamp timestamp, string previousHash, string? hash = null)
    {
        Collection = collection;
        Key = key;
        Operation = operation;
        Payload = payload;
        Timestamp = timestamp;
        PreviousHash = previousHash ?? string.Empty;
        
        // If hash is provided (e.g. from DB or Network), use it.
        // Otherwise (new local entry), compute it.
        Hash = hash ?? ComputeHash();
    }

    /// <summary>
    /// Verifies if the stored Hash matches the content.
    /// </summary>
    public bool IsValid()
    {
        return Hash == ComputeHash();
    }

    private string ComputeHash()
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var sb = new System.Text.StringBuilder();
        
        sb.Append(Collection);
        sb.Append('|');
        sb.Append(Key);
        sb.Append('|');
        sb.Append(Operation);
        sb.Append('|');
        if (Payload.HasValue) sb.Append(Payload.Value.GetRawText());
        sb.Append('|');
        sb.Append(Timestamp.ToString()); // Ensure consistent string representation
        sb.Append('|');
        sb.Append(PreviousHash);

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        var hashBytes = sha256.ComputeHash(bytes);
        
        // Convert to hex string
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
}
