using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using EntglDb.Network;

namespace EntglDb.AspNet.HostedServices;

/// <summary>
/// Hosted service that manages the lifecycle of the TCP sync server.
/// </summary>
public class TcpSyncServerHostedService : IHostedService
{
    private readonly ISyncServer _syncServer;
    private readonly ILogger<TcpSyncServerHostedService> _logger;

    public TcpSyncServerHostedService(
        ISyncServer syncServer,
        ILogger<TcpSyncServerHostedService> logger)
    {
        _syncServer = syncServer;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting TCP Sync Server...");
        await _syncServer.Start();
        _logger.LogInformation("TCP Sync Server started successfully");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping TCP Sync Server...");
        await _syncServer.Stop();
        _logger.LogInformation("TCP Sync Server stopped");
    }
}
