using System.Collections.Generic;
using System.Linq;

namespace EntglDb.Core.Sync;

/// <summary>
/// Tracks the highest contiguous sequence number received from each node.
/// Used to detect gaps in the oplog and avoid re-requesting already synchronized entries.
/// </summary>
public class NodeSequenceTracker
{
    private readonly Dictionary<string, long> _highestContiguousSequence = new();
    private readonly object _lock = new object();

    /// <summary>
    /// Seeds the tracker with persistent state from the database.
    /// This prevents re-requesting gaps that were already filled.
    /// </summary>
    public void SeedFromPersistentState(Dictionary<string, long> nodeSequences)
    {
        lock (_lock)
        {
            foreach (var kvp in nodeSequences)
            {
                _highestContiguousSequence[kvp.Key] = kvp.Value;
            }
        }
    }

    /// <summary>
    /// Gets the highest contiguous sequence number for a node.
    /// Returns 0 if no sequence has been recorded for the node.
    /// </summary>
    public long GetHighestContiguousSequence(string nodeId)
    {
        lock (_lock)
        {
            return _highestContiguousSequence.TryGetValue(nodeId, out var seq) ? seq : 0;
        }
    }

    /// <summary>
    /// Updates the highest contiguous sequence for a node.
    /// Should be called after successfully applying a batch of entries.
    /// </summary>
    public void UpdateContiguousSequence(string nodeId, long sequenceNumber)
    {
        lock (_lock)
        {
            if (!_highestContiguousSequence.TryGetValue(nodeId, out var current) || sequenceNumber > current)
            {
                _highestContiguousSequence[nodeId] = sequenceNumber;
            }
        }
    }

    /// <summary>
    /// Gets all tracked node sequences.
    /// </summary>
    public Dictionary<string, long> GetAllSequences()
    {
        lock (_lock)
        {
            return new Dictionary<string, long>(_highestContiguousSequence);
        }
    }
}
