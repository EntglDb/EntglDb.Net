using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using EntglDb.Network;

namespace EntglDb.Sample.Console;

public class EntglDbNodeService : IHostedService
{
    private readonly EntglDbNode _node;
    private readonly ILogger<EntglDbNodeService> _logger;

    public EntglDbNodeService(EntglDbNode node, ILogger<EntglDbNodeService> logger)
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
