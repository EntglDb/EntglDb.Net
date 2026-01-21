using System;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core.Sync;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EntglDb.Network.HostedServices;

/// <summary>
/// Background service that performs reconciliation on startup if configured.
/// Runs gap detection and fills missing operations automatically.
/// </summary>
public class ReconciliationBackgroundService : IHostedService
{
    private readonly IReconciliationService _reconciliationService;
    private readonly ReconciliationOptions _options;
    private readonly ILogger<ReconciliationBackgroundService> _logger;
    private CancellationTokenSource? _cts;

    public ReconciliationBackgroundService(
        IReconciliationService reconciliationService,
        IOptions<ReconciliationOptions> options,
        ILogger<ReconciliationBackgroundService> logger)
    {
        _reconciliationService = reconciliationService;
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = ExecuteAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    protected async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableOnStartup)
        {
            _logger.LogInformation("Reconciliation on startup is disabled");
            return;
        }

        try
        {
            // Wait a bit for other services to initialize
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            _logger.LogInformation("Starting automatic reconciliation on startup");

            // Analyze first
            var analysis = await _reconciliationService.AnalyzeDatabaseAsync(stoppingToken);
            
            if (analysis.RecommendReconciliation)
            {
                _logger.LogWarning("Reconciliation recommended: {Reason}", analysis.Reason);
                _logger.LogInformation("Legacy data: {Percentage:P1} ({Count}/{Total} operations)", 
                    analysis.LegacyDataPercentage,
                    analysis.OperationsWithoutSequence,
                    analysis.TotalOperations);

                // Perform full reconciliation
                var result = await _reconciliationService.PerformFullReconciliationAsync(stoppingToken);

                if (result.Success)
                {
                    _logger.LogInformation(
                        "Reconciliation completed successfully. Contacted {Peers} peers, detected {Gaps} gaps in {Duration}",
                        result.PeersContacted,
                        result.GapsDetected,
                        result.Duration);
                }
                else
                {
                    _logger.LogError("Reconciliation failed: {Errors}", string.Join(", ", result.Errors));
                }
            }
            else
            {
                _logger.LogInformation("Database analysis: No reconciliation needed. {Reason}", analysis.Reason);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Reconciliation cancelled during shutdown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during automatic reconciliation");
        }
    }
}
