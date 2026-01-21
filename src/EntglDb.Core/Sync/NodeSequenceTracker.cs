using System.Collections.Generic;
using System.Linq;

namespace EntglDb.Core.Sync;

/// <summary>
/// Tracks the sequence numbers received from each peer node.
/// Used for gap detection to identify missing operations.
/// </summary>
public class NodeSequenceTracker
{
    /// <summary>
    /// NodeId -> Highest contiguous sequence number received
    /// </summary>
    private readonly Dictionary<string, long> _nodeSequences = new();

    /// <summary>
    /// NodeId -> Set of sequence numbers received (for gap detection)
    /// </summary>
    private readonly Dictionary<string, SortedSet<long>> _receivedSequences = new();

    private readonly object _lock = new();

    /// <summary>
    /// Records a sequence number received from a specific node.
    /// </summary>
    /// <param name="nodeId">The node that generated the operation</param>
    /// <param name="sequenceNumber">The sequence number of the operation</param>
    public void RecordSequence(string nodeId, long sequenceNumber)
    {
        lock (_lock)
        {
            if (!_receivedSequences.ContainsKey(nodeId))
            {
                _receivedSequences[nodeId] = new SortedSet<long>();
                _nodeSequences[nodeId] = 0;
            }

            _receivedSequences[nodeId].Add(sequenceNumber);

            // Update highest contiguous sequence
            UpdateHighestContiguous(nodeId);
        }
    }

    /// <summary>
    /// Detects gaps in the sequence for a specific node.
    /// </summary>
    /// <param name="nodeId">The node to check for gaps</param>
    /// <param name="latestKnownSequence">The latest sequence number we know exists on the remote node</param>
    /// <returns>List of missing sequence numbers (gaps)</returns>
    public List<long> DetectGaps(string nodeId, long latestKnownSequence)
    {
        lock (_lock)
        {
            if (!_receivedSequences.ContainsKey(nodeId))
            {
                // No sequences received yet - need all from 1 to latestKnownSequence
                return Enumerable.Range(1, (int)latestKnownSequence).Select(i => (long)i).ToList();
            }

            var received = _receivedSequences[nodeId];
            var gaps = new List<long>();

            for (long i = 1; i <= latestKnownSequence; i++)
            {
                if (!received.Contains(i))
                {
                    gaps.Add(i);
                }
            }

            return gaps;
        }
    }

    /// <summary>
    /// Gets the highest contiguous sequence number received from a node.
    /// </summary>
    public long GetHighestContiguous(string nodeId)
    {
        lock (_lock)
        {
            return _nodeSequences.TryGetValue(nodeId, out var seq) ? seq : 0;
        }
    }

    /// <summary>
    /// Gets all tracked nodes.
    /// </summary>
    public List<string> GetTrackedNodes()
    {
        lock (_lock)
        {
            return _nodeSequences.Keys.ToList();
        }
    }

    private void UpdateHighestContiguous(string nodeId)
    {
        var received = _receivedSequences[nodeId];
        long current = _nodeSequences[nodeId];

        // Find the highest contiguous sequence starting from current + 1
        while (received.Contains(current + 1))
        {
            current++;
        }

        _nodeSequences[nodeId] = current;
    }

    /// <summary>
    /// Clears old sequence numbers to prevent unbounded memory growth.
    /// Call periodically to clean up sequences below the contiguous threshold.
    /// </summary>
    public void CompactHistory(string nodeId, long keepAbove)
    {
        lock (_lock)
        {
            if (!_receivedSequences.ContainsKey(nodeId))
                return;

            var received = _receivedSequences[nodeId];
            var toRemove = received.Where(s => s < keepAbove).ToList();
            
            foreach (var seq in toRemove)
            {
                received.Remove(seq);
            }
        }
    }
}
