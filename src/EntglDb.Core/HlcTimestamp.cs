using System;

namespace EntglDb.Core;

/// <summary>
/// Represents a Hybrid Logical Clock timestamp.
/// Provides a Total Ordering of events in a distributed system.
/// Implements value semantics and comparable interfaces.
/// </summary>
public readonly struct HlcTimestamp : IComparable<HlcTimestamp>, IComparable, IEquatable<HlcTimestamp>
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
}