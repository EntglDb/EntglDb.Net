using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using EntglDb.Core.Storage;

namespace EntglDb.Core.Sync;

/// <summary>
/// Service for detecting gaps in the oplog by tracking contiguous sequences per node.
/// Persists state to avoid re-requesting gaps after restart.
/// </summary>
public class GapDetectionService
{
    private readonly IPeerStore _store;
    private readonly NodeSequenceTracker _tracker;
    private readonly ILogger<GapDetectionService> _logger;
    private bool _isSeeded = false;
    private readonly object _seedLock = new object();

    public GapDetectionService(
        IPeerStore store,
        ILogger<GapDetectionService>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _tracker = new NodeSequenceTracker();
        _logger = logger ?? NullLogger<GapDetectionService>.Instance;
    }

    /// <summary>
    /// Seeds the gap tracker from persistent oplog data.
    /// Calculates the highest contiguous sequence per node from the oplog.
    /// This is called on first use to restore state and prevent repeated gap requests.
    /// </summary>
    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        lock (_seedLock)
        {
            if (_isSeeded)
                return;
        }

        try
        {
            _logger.LogInformation("Seeding gap detection from persistent oplog data");

            // Get all oplog entries to analyze
            var allEntries = await _store.GetOplogAfterAsync(new HlcTimestamp(0, 0, ""), cancellationToken);
            
            // Group by node and find highest contiguous timestamp (using physical time as sequence)
            var nodeGroups = allEntries
                .GroupBy(e => e.Timestamp.NodeId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Max(e => e.Timestamp.PhysicalTime)
                );

            _tracker.SeedFromPersistentState(nodeGroups);

            lock (_seedLock)
            {
                _isSeeded = true;
            }

            _logger.LogInformation("Gap detection seeded with {NodeCount} nodes", nodeGroups.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed gap detection from persistent state");
            throw;
        }
    }

    /// <summary>
    /// Gets the highest contiguous timestamp for a node.
    /// </summary>
    public HlcTimestamp GetHighestContiguousTimestamp(string nodeId)
    {
        var sequence = _tracker.GetHighestContiguousSequence(nodeId);
        return new HlcTimestamp(sequence, 0, nodeId);
    }

    /// <summary>
    /// Updates the contiguous sequence after successfully applying a batch of entries.
    /// This prevents the same entries from being requested again.
    /// </summary>
    public void UpdateAfterApplyBatch(IEnumerable<OplogEntry> entries)
    {
        if (!entries.Any())
            return;

        // Group entries by node and update the highest timestamp for each
        var nodeGroups = entries.GroupBy(e => e.Timestamp.NodeId);
        
        foreach (var group in nodeGroups)
        {
            var maxTimestamp = group.Max(e => e.Timestamp.PhysicalTime);
            _tracker.UpdateContiguousSequence(group.Key, maxTimestamp);
            _logger.LogDebug("Updated contiguous sequence for node {NodeId} to {Sequence}", group.Key, maxTimestamp);
        }
    }

    /// <summary>
    /// Detects if there are gaps that need to be filled.
    /// Returns true if the remote timestamp is ahead but we haven't received all entries.
    /// </summary>
    public bool HasGap(string nodeId, HlcTimestamp remoteTimestamp)
    {
        var localHighest = GetHighestContiguousTimestamp(nodeId);
        
        // If remote is ahead, we potentially have a gap
        if (remoteTimestamp.CompareTo(localHighest) > 0)
        {
            _logger.LogDebug("Potential gap detected for node {NodeId}: local={Local}, remote={Remote}", 
                nodeId, localHighest, remoteTimestamp);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets all tracked node sequences for persistence.
    /// </summary>
    public Dictionary<string, long> GetAllSequences()
    {
        return _tracker.GetAllSequences();
    }
}
