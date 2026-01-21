using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core.Storage;
using EntglDb.Core.Sync;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace EntglDb.Network.HealthChecks;

/// <summary>
/// Health check that verifies database migrations and gap detection status.
/// </summary>
public class EntglDbMigrationHealthCheck : IHealthCheck
{
    private readonly IPeerStore _store;
    private readonly IGapDetectionService? _gapDetection;
    private readonly ILogger<EntglDbMigrationHealthCheck> _logger;

    public EntglDbMigrationHealthCheck(
        IPeerStore store,
        IGapDetectionService? gapDetection,
        ILogger<EntglDbMigrationHealthCheck> logger)
    {
        _store = store;
        _gapDetection = gapDetection;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = new System.Collections.Generic.Dictionary<string, object>();

            // Check if gap detection is working (sequence numbers available)
            var currentSeq = await _store.GetCurrentSequenceNumberAsync(cancellationToken);
            var peerSequences = await _store.GetPeerSequenceNumbersAsync(cancellationToken);

            data["CurrentSequenceNumber"] = currentSeq;
            data["PeerNodesTracked"] = peerSequences.Count;
            data["TotalOperationsTracked"] = peerSequences.Values.Sum();

            // Check gap detection status if available
            if (_gapDetection != null)
            {
                var status = _gapDetection.GetStatus();
                data["NodesWithGapTracking"] = status.HighestContiguousPerNode.Count;
                data["KnownGaps"] = status.TotalGapsDetected;
            }

            // Determine health status
            if (currentSeq == 0 && peerSequences.Count == 0)
            {
                // New database or legacy database without any operations yet
                return HealthCheckResult.Healthy(
                    "Database initialized. No operations tracked yet.",
                    data);
            }

            if (currentSeq > 0)
            {
                // Gap detection is working
                return HealthCheckResult.Healthy(
                    $"Gap detection operational. Current sequence: {currentSeq}",
                    data);
            }

            if (peerSequences.Values.Any(v => v > 0))
            {
                // We have received data from peers but not generating our own sequences
                // This might be a legacy node or migration in progress
                return HealthCheckResult.Degraded(
                    "Receiving peer data but not generating sequence numbers. Migration may be needed.",
                    null,
                    data);
            }

            return HealthCheckResult.Healthy("Database operational", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return HealthCheckResult.Unhealthy(
                "Failed to check migration status",
                ex);
        }
    }
}
