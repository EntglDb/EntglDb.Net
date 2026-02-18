using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace EntglDb.Core;

public enum OperationType
{
    Put,
    Delete
}

public static class OplogEntryExtensions
{
    public static string ComputeHash(this OplogEntry entry)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var sb = new System.Text.StringBuilder();

        sb.Append(entry.Collection);
        sb.Append('|');
        sb.Append(entry.Key);
        sb.Append('|');
        // Ensure stable string representation for Enum (integer value)
        sb.Append(((int)entry.Operation).ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append('|');
        // Payload excluded from hash to avoid serialization non-determinism
        // sb.Append(entry.Payload...); 
        sb.Append('|');
        // Timestamp.ToString() is now Invariant
        sb.Append(entry.Timestamp.ToString()); 
        sb.Append('|');
        sb.Append(entry.PreviousHash);

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        var hashBytes = sha256.ComputeHash(bytes);

        // Convert to hex string
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
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
        Hash = hash ?? this.ComputeHash();
    }

    /// <summary>
    /// Verifies if the stored Hash matches the content.
    /// </summary>
    public bool IsValid()
    {
        return Hash == this.ComputeHash();
    }
}
