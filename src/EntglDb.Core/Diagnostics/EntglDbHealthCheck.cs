using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using EntglDb.Core.Storage;

namespace EntglDb.Core.Diagnostics;

/// <summary>
/// Provides health check functionality.
/// </summary>
public class EntglDbHealthCheck : IEntglDbHealthCheck
{
    private readonly IPeerStore _store;
    private readonly ISyncStatusTracker _syncTracker;
    private readonly ILogger<EntglDbHealthCheck> _logger;

    public EntglDbHealthCheck(
        IPeerStore store,
        ISyncStatusTracker syncTracker,
        ILogger<EntglDbHealthCheck>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _syncTracker = syncTracker ?? throw new ArgumentNullException(nameof(syncTracker));
        _logger = logger ?? NullLogger<EntglDbHealthCheck>.Instance;
    }

    /// <summary>
    /// Performs a comprehensive health check.
    /// </summary>
    public async Task<HealthStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        var status = new HealthStatus();

        // Check database health
        try
        {
            // Try to get latest timestamp (simple database operation)
            var timestamp = await _store.GetLatestTimestampAsync(cancellationToken);
            status.DatabaseHealthy = true;
            _logger.LogDebug("Database health check passed (latest timestamp: {Timestamp})", timestamp);
        }
        catch (Exception ex)
        {
            status.DatabaseHealthy = false;
            status.Errors.Add($"Database check failed: {ex.Message}");
            _logger.LogError(ex, "Database health check failed");
        }

        // Get sync status
        var syncStatus = _syncTracker.GetStatus();
        status.NetworkHealthy = syncStatus.IsOnline;
        status.ConnectedPeers = syncStatus.ActivePeers.Count(p => p.IsConnected);
        status.LastSyncTime = syncStatus.LastSyncTime;

        // Add error messages from sync tracker
        foreach (var error in syncStatus.SyncErrors.Take(5)) // Last 5 errors
        {
            status.Errors.Add($"{error.Timestamp:yyyy-MM-dd HH:mm:ss} - {error.Message}");
        }

        // Add metadata
        status.Metadata["TotalDocumentsSynced"] = syncStatus.TotalDocumentsSynced;
        status.Metadata["TotalBytesTransferred"] = syncStatus.TotalBytesTransferred;
        status.Metadata["ActivePeers"] = syncStatus.ActivePeers.Count;

        _logger.LogInformation("Health check completed: Database={DbHealth}, Network={NetHealth}, Peers={Peers}",
            status.DatabaseHealthy, status.NetworkHealthy, status.ConnectedPeers);

        return status;
    }
}
