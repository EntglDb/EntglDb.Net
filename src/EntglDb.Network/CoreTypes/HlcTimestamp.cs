using System;

namespace EntglDb.Core;

/// <summary>
/// Represents a Hybrid Logical Clock timestamp.
/// Provides a Total Ordering of events in a distributed system.
/// Implements value semantics and comparable interfaces.
/// </summary>
public readonly struct HlcTimestamp : IComparable<HlcTimestamp>, IComparable, IEquatable<HlcTimestamp>
#if NET6_0_OR_GREATER
    , ISpanFormattable
#endif
#if NET8_0_OR_GREATER
    , IUtf8SpanFormattable
#endif
{
    public long PhysicalTime { get; }
    public int LogicalCounter { get; }
    public string NodeId { get; }

    public HlcTimestamp(long physicalTime, int logicalCounter, string nodeId)
    {
        PhysicalTime = physicalTime;
        LogicalCounter = logicalCounter;
        NodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
    }

    /// <summary>
    /// Compares two timestamps to establish a total order.
    /// Order: PhysicalTime -> LogicalCounter -> NodeId (lexicographical tie-breaker).
    /// </summary>
    public int CompareTo(HlcTimestamp other)
    {
        int timeComparison = PhysicalTime.CompareTo(other.PhysicalTime);
        if (timeComparison != 0) return timeComparison;

        int counterComparison = LogicalCounter.CompareTo(other.LogicalCounter);
        if (counterComparison != 0) return counterComparison;

        // Use Ordinal comparison for consistent tie-breaking across cultures/platforms
        return string.Compare(NodeId, other.NodeId, StringComparison.Ordinal);
    }

    public int CompareTo(object? obj)
    {
        if (obj is null) return 1;
        if (obj is HlcTimestamp other) return CompareTo(other);
        throw new ArgumentException($"Object must be of type {nameof(HlcTimestamp)}");
    }

    public bool Equals(HlcTimestamp other)
    {
        return PhysicalTime == other.PhysicalTime &&
               LogicalCounter == other.LogicalCounter &&
               string.Equals(NodeId, other.NodeId, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is HlcTimestamp other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = PhysicalTime.GetHashCode();
            hashCode = (hashCode * 397) ^ LogicalCounter;
            // Ensure HashCode uses the same comparison logic as Equals/CompareTo
            // Handle null NodeId gracefully (possible via default(HlcTimestamp))
            hashCode = (hashCode * 397) ^ (NodeId != null ? StringComparer.Ordinal.GetHashCode(NodeId) : 0);
            return hashCode;
        }
    }

    public static bool operator ==(HlcTimestamp left, HlcTimestamp right) => left.Equals(right);
    public static bool operator !=(HlcTimestamp left, HlcTimestamp right) => !left.Equals(right);

    // Standard comparison operators making usage in SyncOrchestrator cleaner (e.g., remote > local)
    public static bool operator <(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) < 0;
    public static bool operator <=(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) <= 0;
    public static bool operator >(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) > 0;
    public static bool operator >=(HlcTimestamp left, HlcTimestamp right) => left.CompareTo(right) >= 0;

    public override string ToString() => FormattableString.Invariant($"{PhysicalTime}:{LogicalCounter}:{NodeId}");

#if NET6_0_OR_GREATER
    /// <summary>
    /// Implements IFormattable (required by ISpanFormattable). Ignores format/provider — always uses invariant format.
    /// </summary>
    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    /// <summary>
    /// Formats into a char span — allows string.Create / interpolation handlers to avoid allocation.
    /// </summary>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        charsWritten = 0;
        if (!PhysicalTime.TryFormat(destination, out int w1, "D", System.Globalization.CultureInfo.InvariantCulture)) return false;
        charsWritten += w1; destination = destination[w1..];
        if (destination.IsEmpty) return false;
        destination[0] = ':'; destination = destination[1..]; charsWritten++;
        if (!LogicalCounter.TryFormat(destination, out int w2, "D", System.Globalization.CultureInfo.InvariantCulture)) return false;
        charsWritten += w2; destination = destination[w2..];
        if (destination.IsEmpty) return false;
        destination[0] = ':'; destination = destination[1..]; charsWritten++;
        if (NodeId == null || NodeId.Length > destination.Length) return false;
        NodeId.AsSpan().CopyTo(destination);
        charsWritten += NodeId.Length;
        return true;
    }
#endif

#if NET8_0_OR_GREATER
    /// <summary>
    /// Formats into a UTF-8 byte span — used by Utf8.TryWrite and similar APIs.
    /// </summary>
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        bytesWritten = 0;
        if (!System.Text.Unicode.Utf8.TryWrite(utf8Destination, System.Globalization.CultureInfo.InvariantCulture,
            $"{PhysicalTime}:{LogicalCounter}:{NodeId}", out int written))
            return false;
        bytesWritten = written;
        return true;
    }
#endif

    /// <summary>
    /// Parses an HlcTimestamp from its string representation without allocating intermediate string arrays.
    /// </summary>
    public static HlcTimestamp Parse(string s)
    {
        if (string.IsNullOrEmpty(s)) throw new ArgumentNullException(nameof(s));
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
        return ParseSpan(s.AsSpan());
#else
        var parts = s.Split(':');
        if (parts.Length != 3) throw new FormatException("Invalid HlcTimestamp format. Expected 'PhysicalTime:LogicalCounter:NodeId'.");
        if (!long.TryParse(parts[0], out var physicalTime))
            throw new FormatException("Invalid PhysicalTime component in HlcTimestamp.");
        if (!int.TryParse(parts[1], out var logicalCounter))
            throw new FormatException("Invalid LogicalCounter component in HlcTimestamp.");
        return new HlcTimestamp(physicalTime, logicalCounter, parts[2]);
#endif
    }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
    /// <summary>
    /// Zero-allocation parse from a ReadOnlySpan&lt;char&gt;. Avoids string.Split() array allocation.
    /// </summary>
    public static HlcTimestamp Parse(ReadOnlySpan<char> s)
    {
        if (s.IsEmpty) throw new ArgumentNullException(nameof(s));
        return ParseSpan(s);
    }

    private static HlcTimestamp ParseSpan(ReadOnlySpan<char> s)
    {
        int firstColon = s.IndexOf(':');
        if (firstColon < 0) throw new FormatException("Invalid HlcTimestamp format. Expected 'PhysicalTime:LogicalCounter:NodeId'.");

        if (!long.TryParse(s[..firstColon], System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var physicalTime))
            throw new FormatException("Invalid PhysicalTime component in HlcTimestamp.");

        var rest = s[(firstColon + 1)..];
        int secondColon = rest.IndexOf(':');
        if (secondColon < 0) throw new FormatException("Invalid HlcTimestamp format. Expected 'PhysicalTime:LogicalCounter:NodeId'.");

        if (!int.TryParse(rest[..secondColon], System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var logicalCounter))
            throw new FormatException("Invalid LogicalCounter component in HlcTimestamp.");

        var nodeId = new string(rest[(secondColon + 1)..]);
        return new HlcTimestamp(physicalTime, logicalCounter, nodeId);
    }
#endif
}