using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Core.Sync;

/// <summary>
/// Service that detects and fills gaps in oplog synchronization.
/// Ensures eventual consistency by identifying missing operations.
/// </summary>
public interface IGapDetectionService
{
    /// <summary>
    /// Detects gaps in the local oplog compared to a peer.
    /// </summary>
    /// <param name="nodeId">The peer node to check against</param>
    /// <param name="peerSequenceNumbers">The sequence numbers available on the peer</param>
    /// <returns>List of sequence numbers that are missing locally</returns>
    Task<List<long>> DetectGapsAsync(string nodeId, Dictionary<string, long> peerSequenceNumbers, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records received oplog entries to update gap tracking.
    /// </summary>
    void RecordReceivedEntries(IEnumerable<OplogEntry> entries);

    /// <summary>
    /// Gets the current state of sequence tracking for diagnostics.
    /// </summary>
    GapDetectionStatus GetStatus();
}

/// <summary>
/// Status of gap detection for diagnostics.
/// </summary>
public class GapDetectionStatus
{
    public Dictionary<string, long> HighestContiguousPerNode { get; set; } = new();
    public Dictionary<string, List<long>> KnownGaps { get; set; } = new();
    public int TotalGapsDetected { get; set; }
}

public class GapDetectionService : IGapDetectionService
{
    private readonly IPeerStore _store;
    private readonly NodeSequenceTracker _tracker;
    private readonly ILogger<GapDetectionService> _logger;

    public GapDetectionService(
        IPeerStore store,
        ILogger<GapDetectionService>? logger = null)
    {
        _store = store;
        _tracker = new NodeSequenceTracker();
        _logger = logger ?? NullLogger<GapDetectionService>.Instance;
    }

    /// <summary>
    /// Detects gaps by comparing peer sequence numbers with local tracking.
    /// </summary>
    public async Task<List<long>> DetectGapsAsync(
        string nodeId, 
        Dictionary<string, long> peerSequenceNumbers, 
        CancellationToken cancellationToken = default)
    {
        if (!peerSequenceNumbers.TryGetValue(nodeId, out var latestPeerSequence))
        {
            _logger.LogDebug("No sequence information available for node {NodeId}", nodeId);
            return new List<long>();
        }

        // Get our local sequence numbers for this peer
        var localSequences = await _store.GetPeerSequenceNumbersAsync(cancellationToken);
        var localLatest = localSequences.TryGetValue(nodeId, out var local) ? local : 0;

        if (latestPeerSequence <= localLatest)
        {
            // We're up to date or ahead - no gaps
            return new List<long>();
        }

        // Detect gaps using the tracker
        var gaps = _tracker.DetectGaps(nodeId, latestPeerSequence);

        if (gaps.Count > 0)
        {
            _logger.LogWarning(
                "Detected {GapCount} gaps in sequence from node {NodeId} (range: {MinGap}-{MaxGap})",
                gaps.Count, nodeId, gaps.Min(), gaps.Max());
        }

        return gaps;
    }

    /// <summary>
    /// Records received entries to update our tracking of what we have.
    /// </summary>
    public void RecordReceivedEntries(IEnumerable<OplogEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.SequenceNumber > 0)
            {
                _tracker.RecordSequence(entry.Timestamp.NodeId, entry.SequenceNumber);
            }
        }
    }

    /// <summary>
    /// Gets diagnostic information about current gap detection state.
    /// </summary>
    public GapDetectionStatus GetStatus()
    {
        var status = new GapDetectionStatus();
        var nodes = _tracker.GetTrackedNodes();

        foreach (var nodeId in nodes)
        {
            var highest = _tracker.GetHighestContiguous(nodeId);
            status.HighestContiguousPerNode[nodeId] = highest;
        }

        return status;
    }

    /// <summary>
    /// Performs periodic cleanup of old sequence tracking data.
    /// Should be called periodically (e.g., every hour).
    /// </summary>
    public void PerformMaintenance()
    {
        var nodes = _tracker.GetTrackedNodes();
        
        foreach (var nodeId in nodes)
        {
            var highest = _tracker.GetHighestContiguous(nodeId);
            // Keep only recent history (last 1000 operations or similar)
            if (highest > 1000)
            {
                _tracker.CompactHistory(nodeId, highest - 1000);
            }
        }

        _logger.LogDebug("Gap detection maintenance completed for {NodeCount} nodes", nodes.Count);
    }
}
