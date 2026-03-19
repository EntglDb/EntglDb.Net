using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Network;

/// <summary>
/// Hosted service that automatically starts and stops the EntglDb node.
/// </summary>
public class EntglDbNodeService : IHostedService
{
    private readonly IEntglDbNode _node;
    private readonly ILogger<EntglDbNodeService> _logger;

    public EntglDbNodeService(IEntglDbNode node, ILogger<EntglDbNodeService> logger)
    {
        _node = node;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting EntglDb Node Service...");
            
            // Check for cancellation before starting
            cancellationToken.ThrowIfCancellationRequested();
            
            await _node.Start();
            _logger.LogInformation("EntglDb Node Service started successfully");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("EntglDb Node Service start was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start EntglDb Node Service");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Stopping EntglDb Node Service...");
            await _node.Stop();
            _logger.LogInformation("EntglDb Node Service stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while stopping EntglDb Node Service");
            // Don't rethrow during shutdown to avoid breaking the shutdown process
        }
    }
}
