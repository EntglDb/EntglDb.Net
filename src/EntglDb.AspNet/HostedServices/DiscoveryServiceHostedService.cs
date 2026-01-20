using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using EntglDb.Network;

namespace EntglDb.AspNet.HostedServices;

/// <summary>
/// Hosted service that manages the lifecycle of the discovery service.
/// </summary>
public class DiscoveryServiceHostedService : IHostedService
{
    private readonly IDiscoveryService _discoveryService;
    private readonly ILogger<DiscoveryServiceHostedService> _logger;

    public DiscoveryServiceHostedService(
        IDiscoveryService discoveryService,
        ILogger<DiscoveryServiceHostedService> logger)
    {
        _discoveryService = discoveryService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Discovery Service...");
        await _discoveryService.Start();
        _logger.LogInformation("Discovery Service started");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Discovery Service...");
        await _discoveryService.Stop();
        _logger.LogInformation("Discovery Service stopped");
    }
}
