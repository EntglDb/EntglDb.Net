using System;

namespace EntglDb.Core;

public readonly struct HlcTimestamp : IComparable<HlcTimestamp>, IEquatable<HlcTimestamp>
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

    public int CompareTo(HlcTimestamp other)
    {
        int timeComparison = PhysicalTime.CompareTo(other.PhysicalTime);
        if (timeComparison != 0) return timeComparison;

        int counterComparison = LogicalCounter.CompareTo(other.LogicalCounter);
        if (counterComparison != 0) return counterComparison;

        return string.Compare(NodeId, other.NodeId, StringComparison.Ordinal);
    }

    public bool Equals(HlcTimestamp other)
    {
        return PhysicalTime == other.PhysicalTime &&
               LogicalCounter == other.LogicalCounter &&
               NodeId == other.NodeId;
    }

    public override bool Equals(object obj)
    {
        return obj is HlcTimestamp other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = PhysicalTime.GetHashCode();
            hashCode = (hashCode * 397) ^ LogicalCounter;
            hashCode = (hashCode * 397) ^ (NodeId != null ? NodeId.GetHashCode() : 0);
            return hashCode;
        }
    }

    public static bool operator ==(HlcTimestamp left, HlcTimestamp right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(HlcTimestamp left, HlcTimestamp right)
    {
        return !left.Equals(right);
    }

    public override string ToString() => $"{PhysicalTime}:{LogicalCounter}:{NodeId}";
}
