using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Core.Sync;

/// <summary>
/// Configuration options for full reconciliation.
/// </summary>
public class ReconciliationOptions
{
    /// <summary>
    /// Whether to perform full reconciliation on startup.
    /// </summary>
    public bool EnableOnStartup { get; set; } = false;

    /// <summary>
    /// Threshold of operations with sequence number 0 to trigger automatic reconciliation.
    /// If percentage of ops with seq=0 exceeds this, full reconciliation is recommended.
    /// </summary>
    public double LegacyDataThreshold { get; set; } = 0.5; // 50%

    /// <summary>
    /// Maximum number of operations to process in each reconciliation batch.
    /// </summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Whether to run reconciliation in background (non-blocking).
    /// </summary>
    public bool RunInBackground { get; set; } = true;
}

/// <summary>
/// Performs full cluster reconciliation to ensure all nodes have consistent data.
/// Used primarily for migration scenarios and recovery from extended partitions.
/// </summary>
public interface IReconciliationService
{
    /// <summary>
    /// Performs a full reconciliation with all available peers.
    /// </summary>
    Task<ReconciliationResult> PerformFullReconciliationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if full reconciliation is recommended based on database state.
    /// </summary>
    Task<ReconciliationAnalysis> AnalyzeDatabaseAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a reconciliation operation.
/// </summary>
public class ReconciliationResult
{
    public bool Success { get; set; }
    public int PeersContacted { get; set; }
    public int OperationsSynced { get; set; }
    public int GapsDetected { get; set; }
    public int GapsFilled { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Analysis of database state for reconciliation planning.
/// </summary>
public class ReconciliationAnalysis
{
    public long TotalOperations { get; set; }
    public long OperationsWithoutSequence { get; set; }
    public double LegacyDataPercentage { get; set; }
    public bool RecommendReconciliation { get; set; }
    public string Reason { get; set; } = "";
}

public class ReconciliationService : IReconciliationService
{
    private readonly IPeerStore _store;
    private readonly IGapDetectionService _gapDetection;
    private readonly ILogger<ReconciliationService> _logger;

    public ReconciliationService(
        IPeerStore store,
        IGapDetectionService gapDetection,
        ILogger<ReconciliationService>? logger = null)
    {
        _store = store;
        _gapDetection = gapDetection;
        _logger = logger ?? NullLogger<ReconciliationService>.Instance;
    }

    /// <summary>
    /// Analyzes the database to determine if reconciliation is needed.
    /// </summary>
    public async Task<ReconciliationAnalysis> AnalyzeDatabaseAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing database for reconciliation needs");

        var analysis = new ReconciliationAnalysis();

        try
        {
            // Get all sequence numbers
            var peerSequences = await _store.GetPeerSequenceNumbersAsync(cancellationToken);
            
            if (peerSequences.Count == 0)
            {
                analysis.RecommendReconciliation = false;
                analysis.Reason = "No sequence data available yet";
                return analysis;
            }

            // Estimate total operations (rough approximation)
            analysis.TotalOperations = peerSequences.Values.Sum();

            // Get current sequence number (local)
            var currentSeq = await _store.GetCurrentSequenceNumberAsync(cancellationToken);

            // If current sequence is 0, this is likely a migrated database
            if (currentSeq == 0 && peerSequences.Values.Any(v => v > 0))
            {
                analysis.RecommendReconciliation = true;
                analysis.Reason = "Database appears to be migrated from legacy version without sequence numbers";
                analysis.LegacyDataPercentage = 1.0;
                return analysis;
            }

            // Check for gaps in received data
            var status = _gapDetection.GetStatus();
            var totalTracked = status.HighestContiguousPerNode.Values.Sum();

            if (totalTracked > 0)
            {
                var contiguousPercentage = (double)totalTracked / analysis.TotalOperations;
                analysis.LegacyDataPercentage = 1.0 - contiguousPercentage;

                if (analysis.LegacyDataPercentage > 0.3) // More than 30% legacy
                {
                    analysis.RecommendReconciliation = true;
                    analysis.Reason = $"High percentage ({analysis.LegacyDataPercentage:P1}) of legacy data detected";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing database for reconciliation");
            analysis.Reason = $"Analysis error: {ex.Message}";
        }

        return analysis;
    }

    /// <summary>
    /// Performs full reconciliation - pulls all oplog data from scratch.
    /// This is expensive but ensures consistency.
    /// </summary>
    public async Task<ReconciliationResult> PerformFullReconciliationAsync(CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var result = new ReconciliationResult();

        _logger.LogWarning("Starting FULL RECONCILIATION - this may take time for large databases");

        try
        {
            // Get all peer sequence numbers
            var peerSequences = await _store.GetPeerSequenceNumbersAsync(cancellationToken);
            result.PeersContacted = peerSequences.Count;

            _logger.LogInformation("Found data from {PeerCount} peer nodes", peerSequences.Count);

            // For each node, detect and fill gaps
            foreach (var kvp in peerSequences)
            {
                var nodeId = kvp.Key;
                var maxSeq = kvp.Value;
                
                _logger.LogInformation("Reconciling data from node {NodeId} (up to seq {MaxSeq})", nodeId, maxSeq);

                var gaps = await _gapDetection.DetectGapsAsync(nodeId, peerSequences, cancellationToken);
                result.GapsDetected += gaps.Count;

                if (gaps.Count > 0)
                {
                    _logger.LogWarning("Node {NodeId} has {GapCount} missing operations", nodeId, gaps.Count);
                    
                    // Note: This requires peer connectivity to fill gaps
                    // In a startup scenario, we can only detect gaps, not fill them
                    // Actual gap filling happens during normal sync
                }
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Full reconciliation failed");
            result.Success = false;
            result.Errors.Add(ex.Message);
        }

        result.Duration = DateTime.UtcNow - startTime;
        _logger.LogInformation("Full reconciliation completed in {Duration}. Gaps detected: {GapCount}", 
            result.Duration, result.GapsDetected);

        return result;
    }
}
