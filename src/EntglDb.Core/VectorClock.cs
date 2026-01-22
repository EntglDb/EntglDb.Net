using System;
using System.Collections.Generic;
using System.Linq;

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

        var allNodes = new HashSet<string>(_clock.Keys.Union(other._clock.Keys), StringComparer.Ordinal);

        foreach (var nodeId in allNodes)
        {
            var thisTs = GetTimestamp(nodeId);
            var otherTs = other.GetTimestamp(nodeId);

            int cmp = thisTs.CompareTo(otherTs);

            if (cmp > 0)
            {
                thisAhead = true;
            }
            else if (cmp < 0)
            {
                otherAhead = true;
            }

            // Early exit if concurrent
            if (thisAhead && otherAhead)
            {
                return CausalityRelation.Concurrent;
            }
        }

        if (thisAhead && !otherAhead)
            return CausalityRelation.StrictlyAhead;
        if (otherAhead && !thisAhead)
            return CausalityRelation.StrictlyBehind;

        return CausalityRelation.Equal;
    }

    /// <summary>
    /// Determines which nodes have updates that this vector clock doesn't have.
    /// Returns node IDs where the other vector clock is ahead.
    /// </summary>
    public IEnumerable<string> GetNodesWithUpdates(VectorClock other)
    {
        var allNodes = new HashSet<string>(_clock.Keys, StringComparer.Ordinal);
        foreach (var nodeId in other._clock.Keys)
        {
            allNodes.Add(nodeId);
        }

        foreach (var nodeId in allNodes)
        {
            var thisTs = GetTimestamp(nodeId);
            var otherTs = other.GetTimestamp(nodeId);

            if (otherTs.CompareTo(thisTs) > 0)
            {
                yield return nodeId;
            }
        }
    }

    /// <summary>
    /// Determines which nodes have updates that the other vector clock doesn't have.
    /// Returns node IDs where this vector clock is ahead.
    /// </summary>
    public IEnumerable<string> GetNodesToPush(VectorClock other)
    {
        var allNodes = new HashSet<string>(_clock.Keys.Union(other._clock.Keys), StringComparer.Ordinal);

        foreach (var nodeId in allNodes)
        {
            var thisTs = GetTimestamp(nodeId);
            var otherTs = other.GetTimestamp(nodeId);

            if (thisTs.CompareTo(otherTs) > 0)
            {
                yield return nodeId;
            }
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

        var entries = _clock.Select(kvp => $"{kvp.Key}:{kvp.Value}");
        return "{" + string.Join(", ", entries) + "}";
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
