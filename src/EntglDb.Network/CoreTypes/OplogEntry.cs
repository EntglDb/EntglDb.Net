using System;
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
#if NET5_0_OR_GREATER
        // Zero-alloc path: write components directly as UTF-8 bytes via ArrayBufferWriter,
        // then compute the hash in-place with stackalloc destination.
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var buffer = new System.Buffers.ArrayBufferWriter<byte>(256);

        AppendUtf8(buffer, entry.Collection);
        AppendByte(buffer, (byte)'|');
        AppendUtf8(buffer, entry.Key);
        AppendByte(buffer, (byte)'|');
        // Stable integer representation of the enum — avoids Enum.ToString() string allocation.
        AppendInt32Utf8(buffer, (int)entry.Operation);
        AppendByte(buffer, (byte)'|');
        // Payload excluded from hash — see comment in original code.
        AppendByte(buffer, (byte)'|');
        // Timestamp formatted inline without a temporary string.
        AppendTimestampUtf8(buffer, entry.Timestamp);
        AppendByte(buffer, (byte)'|');
        AppendUtf8(buffer, entry.PreviousHash);

        Span<byte> hashDest = stackalloc byte[32];
        sha256.TryComputeHash(buffer.WrittenSpan, hashDest, out _);

#if NET7_0_OR_GREATER
        return Convert.ToHexStringLower(hashDest);
#else
        return Convert.ToHexString(hashDest).ToLowerInvariant();
#endif

        static void AppendUtf8(System.Buffers.ArrayBufferWriter<byte> w, string? s)
        {
            if (string.IsNullOrEmpty(s)) return;
            int maxBytes = System.Text.Encoding.UTF8.GetMaxByteCount(s.Length);
            var span = w.GetSpan(maxBytes);
            int written = System.Text.Encoding.UTF8.GetBytes(s.AsSpan(), span);
            w.Advance(written);
        }

        static void AppendByte(System.Buffers.ArrayBufferWriter<byte> w, byte b)
        {
            w.GetSpan(1)[0] = b;
            w.Advance(1);
        }

        static void AppendInt32Utf8(System.Buffers.ArrayBufferWriter<byte> w, int value)
        {
            // Write decimal representation of int directly as ASCII/UTF-8 bytes.
            Span<byte> tmp = stackalloc byte[12];
            if (!System.Buffers.Text.Utf8Formatter.TryFormat(value, tmp, out int written)) return;
            tmp[..written].CopyTo(w.GetSpan(written));
            w.Advance(written);
        }

        static void AppendTimestampUtf8(System.Buffers.ArrayBufferWriter<byte> w, HlcTimestamp ts)
        {
            // Format "PhysicalTime:LogicalCounter:NodeId" without a heap string.
            AppendInt64Utf8(w, ts.PhysicalTime);
            AppendByte(w, (byte)':');
            AppendInt32Utf8(w, ts.LogicalCounter);
            AppendByte(w, (byte)':');
            AppendUtf8(w, ts.NodeId);
        }

        static void AppendInt64Utf8(System.Buffers.ArrayBufferWriter<byte> w, long value)
        {
            Span<byte> tmp = stackalloc byte[22];
            if (!System.Buffers.Text.Utf8Formatter.TryFormat(value, tmp, out int written)) return;
            tmp[..written].CopyTo(w.GetSpan(written));
            w.Advance(written);
        }
#else
        // Fallback for netstandard2.0 and earlier targets.
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var sb = new System.Text.StringBuilder();

        sb.Append(entry.Collection);
        sb.Append('|');
        sb.Append(entry.Key);
        sb.Append('|');
        sb.Append(((int)entry.Operation).ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append('|');
        sb.Append('|');
        sb.Append(entry.Timestamp.ToString());
        sb.Append('|');
        sb.Append(entry.PreviousHash);

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        var hashBytes = sha256.ComputeHash(bytes);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
#endif
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
