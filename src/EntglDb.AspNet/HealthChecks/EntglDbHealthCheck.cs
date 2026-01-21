using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using EntglDb.Core.Storage;

namespace EntglDb.AspNet.HealthChecks;

/// <summary>
/// Health check for EntglDb persistence layer.
/// Verifies that the database connection is healthy.
/// </summary>
public class EntglDbHealthCheck : IHealthCheck
{
    private readonly IPeerStore _store;

    public EntglDbHealthCheck(IPeerStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Simple health check: try to get latest timestamp
            var timestamp = await _store.GetLatestTimestampAsync(cancellationToken);
            
            return HealthCheckResult.Healthy(
                $"EntglDb is healthy. Latest timestamp: {timestamp.PhysicalTime}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "EntglDb persistence layer is unavailable",
                exception: ex);
        }
    }
}
