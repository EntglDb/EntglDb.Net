using System;
using EntglDb.Core.Sync;
using EntglDb.Network.HealthChecks;
using EntglDb.Network.HostedServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EntglDb.Network.Extensions;

/// <summary>
/// Extension methods for registering EntglDB Network services including gap detection and reconciliation.
/// </summary>
public static class EntglDbNetworkExtensions
{
    /// <summary>
    /// Adds EntglDB gap detection and reconciliation services with optional automatic reconciliation on startup.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configureOptions">Optional configuration for reconciliation behavior</param>
    public static IServiceCollection AddEntglDbReconciliation(
        this IServiceCollection services,
        Action<ReconciliationOptions>? configureOptions = null)
    {
        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // Register gap detection service
        services.AddSingleton<IGapDetectionService, GapDetectionService>();

        // Register reconciliation service
        services.AddSingleton<IReconciliationService, ReconciliationService>();

        // Add background service if configured
        var options = new ReconciliationOptions();
        configureOptions?.Invoke(options);

        if (options.EnableOnStartup)
        {
            services.AddHostedService<ReconciliationBackgroundService>();
        }

        return services;
    }

    /// <summary>
    /// Adds EntglDB migration and gap detection health check.
    /// </summary>
    /// <param name="builder">The health checks builder</param>
    /// <param name="name">Optional name for the health check</param>
    /// <param name="failureStatus">Optional failure status</param>
    /// <param name="tags">Optional tags</param>
    public static IHealthChecksBuilder AddEntglDbMigrationCheck(
        this IHealthChecksBuilder builder,
        string? name = null,
        HealthStatus? failureStatus = null,
        string[]? tags = null)
    {
        return builder.AddCheck<EntglDbMigrationHealthCheck>(
            name ?? "entgldb_migration",
            failureStatus,
            tags ?? new[] { "entgldb", "database", "migration", "gap-detection" });
    }
}
