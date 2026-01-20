using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using EntglDb.Core.Network;
using EntglDb.Network;

namespace EntglDb.AspNet.Services;

/// <summary>
/// No-op implementation of IDiscoveryService for server scenarios.
/// Does not perform UDP broadcast discovery - relies on explicit peer configuration.
/// </summary>
public class NoOpDiscoveryService : IDiscoveryService
{
    private readonly ILogger<NoOpDiscoveryService> _logger;

    public NoOpDiscoveryService(ILogger<NoOpDiscoveryService>? logger = null)
    {
        _logger = logger ?? NullLogger<NoOpDiscoveryService>.Instance;
    }

    public IEnumerable<PeerNode> GetActivePeers()
    {
        return Array.Empty<PeerNode>();
    }

    public Task Start()
    {
        _logger.LogInformation("NoOpDiscoveryService started (passive mode - no UDP discovery)");
        return Task.CompletedTask;
    }

    public Task Stop()
    {
        _logger.LogInformation("NoOpDiscoveryService stopped");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _logger.LogDebug("NoOpDiscoveryService disposed");
    }
}
