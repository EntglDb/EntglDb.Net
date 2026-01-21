using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using EntglDb.Network;

namespace EntglDb.AspNet.Services;

/// <summary>
/// No-op implementation of ISyncOrchestrator for server scenarios.
/// Does not initiate outbound sync - only responds to incoming sync requests.
/// </summary>
public class NoOpSyncOrchestrator : ISyncOrchestrator
{
    private readonly ILogger<NoOpSyncOrchestrator> _logger;

    public NoOpSyncOrchestrator(ILogger<NoOpSyncOrchestrator>? logger = null)
    {
        _logger = logger ?? NullLogger<NoOpSyncOrchestrator>.Instance;
    }

    public Task Start()
    {
        _logger.LogInformation("NoOpSyncOrchestrator started (respond-only mode - no outbound sync)");
        return Task.CompletedTask;
    }

    public Task Stop()
    {
        _logger.LogInformation("NoOpSyncOrchestrator stopped");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _logger.LogDebug("NoOpSyncOrchestrator disposed");
    }
}
