using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using EntglDb.Network;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Test.Avalonia;

public class EntglDbNodeService : IHostedService
{
    private readonly IEntglDbNode _node;
    private readonly ILogger<EntglDbNodeService> _logger;

    public EntglDbNodeService(IEntglDbNode node, ILogger<EntglDbNodeService> logger)
    {
        _node = node;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting EntglDb Node Service...");
        _node.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping EntglDb Node Service...");
        _node.Stop();
        return Task.CompletedTask;
    }
}
