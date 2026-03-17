using System;
using System.Collections.Generic;
using System.Text;

namespace EntglDb.Core;

/// <summary>
/// Represents a Vector Clock for tracking causality in a distributed system.
/// Maps NodeId -> HlcTimestamp to track the latest known state of each node.
/// </summary>
public class VectorClock
{
    private readonly Dictionary<string, HlcTimestamp> _clock;

    public VectorClock()
    {
        _clock = new Dictionary<string, HlcTimestamp>(StringComparer.Ordinal);
    }

    public VectorClock(Dictionary<string, HlcTimestamp> clock)
    {
        _clock = new Dictionary<string, HlcTimestamp>(clock, StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets all node IDs in this vector clock.
    /// </summary>
    public IEnumerable<string> NodeIds => _clock.Keys;

    /// <summary>
    /// Gets the timestamp for a specific node, or default if not present.
    /// </summary>
    public HlcTimestamp GetTimestamp(string nodeId)
    {
        return _clock.TryGetValue(nodeId, out var ts) ? ts : default;
    }

    /// <summary>
    /// Sets or updates the timestamp for a specific node.
    /// </summary>
    public void SetTimestamp(string nodeId, HlcTimestamp timestamp)
    {
        _clock[nodeId] = timestamp;
    }

    /// <summary>
    /// Merges another vector clock into this one, taking the maximum timestamp for each node.
    /// </summary>
    public void Merge(VectorClock other)
    {
        foreach (var nodeId in other.NodeIds)
        {
            var otherTs = other.GetTimestamp(nodeId);
            if (!_clock.TryGetValue(nodeId, out var currentTs) || otherTs.CompareTo(currentTs) > 0)
            {
                _clock[nodeId] = otherTs;
            }
        }
    }

    /// <summary>
    /// Compares this vector clock with another to determine causality.
    /// Returns:
    ///  - Positive: This is strictly ahead (dominates other)
    ///  - Negative: Other is strictly ahead (other dominates this)
    ///  - Zero: Concurrent (neither dominates)
    /// </summary>
    public CausalityRelation CompareTo(VectorClock other)
    {
        bool thisAhead = false;
        bool otherAhead = false;

        // Iterate this clock — covers all node IDs known to us
        foreach (var kvp in _clock)
        {
            var thisTs = kvp.Value;
            var otherTs = other.GetTimestamp(kvp.Key);

            int cmp = thisTs.CompareTo(otherTs);
            if (cmp > 0) thisAhead = true;
            else if (cmp < 0) otherAhead = true;

            if (thisAhead && otherAhead) return CausalityRelation.Concurrent;
        }

        // Iterate nodes in other that are NOT in this clock
        // (GetTimestamp returns default for missing — those are always 0, so other is ahead)
        foreach (var kvp in other._clock)
        {
            if (!_clock.ContainsKey(kvp.Key))
            {
                // this has default(0), other has kvp.Value
                int cmp = default(HlcTimestamp).CompareTo(kvp.Value);
                if (cmp > 0) thisAhead = true;
                else if (cmp < 0) otherAhead = true;

                if (thisAhead && otherAhead) return CausalityRelation.Concurrent;
            }
        }

        if (thisAhead && !otherAhead) return CausalityRelation.StrictlyAhead;
        if (otherAhead && !thisAhead) return CausalityRelation.StrictlyBehind;
        return CausalityRelation.Equal;
    }

    /// <summary>
    /// Determines which nodes have updates that this vector clock doesn't have.
    /// Returns node IDs where the other vector clock is ahead.
    /// </summary>
    public IEnumerable<string> GetNodesWithUpdates(VectorClock other)
    {
        // Only need to iterate other's nodes: if other has a node not in this, default(0) < other's ts.
        // Nodes only in this clock cannot be "other is ahead" by definition.
        foreach (var kvp in other._clock)
        {
            var thisTs = GetTimestamp(kvp.Key);
            if (kvp.Value.CompareTo(thisTs) > 0)
                yield return kvp.Key;
        }
    }

    /// <summary>
    /// Determines which nodes have updates that the other vector clock doesn't have.
    /// Returns node IDs where this vector clock is ahead.
    /// </summary>
    public IEnumerable<string> GetNodesToPush(VectorClock other)
    {
        // Only need to iterate this clock's nodes: if this has a node not in other, default(0) < this's ts.
        // Nodes only in other cannot be "this is ahead" by definition.
        foreach (var kvp in _clock)
        {
            var otherTs = other.GetTimestamp(kvp.Key);
            if (kvp.Value.CompareTo(otherTs) > 0)
                yield return kvp.Key;
        }
    }

    /// <summary>
    /// Creates a copy of this vector clock.
    /// </summary>
    public VectorClock Clone()
    {
        return new VectorClock(new Dictionary<string, HlcTimestamp>(_clock, StringComparer.Ordinal));
    }

    public override string ToString()
    {
        if (_clock.Count == 0)
            return "{}";

        var sb = new StringBuilder();
        sb.Append('{');
        bool first = true;
        foreach (var kvp in _clock)
        {
            if (!first) sb.Append(", ");
            sb.Append(kvp.Key).Append(':').Append(kvp.Value);
            first = false;
        }
        sb.Append('}');
        return sb.ToString();
    }
}

/// <summary>
/// Represents the causality relationship between two vector clocks.
/// </summary>
public enum CausalityRelation
{
    /// <summary>Both vector clocks are equal.</summary>
    Equal,
    /// <summary>This vector clock is strictly ahead (dominates).</summary>
    StrictlyAhead,
    /// <summary>This vector clock is strictly behind (dominated).</summary>
    StrictlyBehind,
    /// <summary>Vector clocks are concurrent (neither dominates).</summary>
    Concurrent
}
