using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core;
using EntglDb.Core.Network;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Network.Leadership;

/// <summary>
/// Implements the Bully algorithm for leader election.
/// The node with the lexicographically smallest NodeId becomes the cloud gateway (leader).
/// Elections run periodically (every 5 seconds) to adapt to cluster changes.
/// </summary>
public class BullyLeaderElectionService : ILeaderElectionService
{
    private readonly IDiscoveryService _discoveryService;
    private readonly IPeerNodeConfigurationProvider _configProvider;
    private readonly ILogger<BullyLeaderElectionService> _logger;
    private readonly TimeSpan _electionInterval;

    private CancellationTokenSource? _cts;
    private string? _localNodeId;
    private string? _currentGatewayNodeId;
    private bool _isCloudGateway;

    public bool IsCloudGateway => _isCloudGateway;
    public string? CurrentGatewayNodeId => _currentGatewayNodeId;

    public event EventHandler<LeadershipChangedEventArgs>? LeadershipChanged;

    /// <summary>
    /// Initializes a new instance of the BullyLeaderElectionService class.
    /// </summary>
    /// <param name="discoveryService">Service providing active peer information.</param>
    /// <param name="configProvider">Provider for local node configuration.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="electionInterval">Interval between elections. Defaults to 5 seconds.</param>
    public BullyLeaderElectionService(
        IDiscoveryService discoveryService,
        IPeerNodeConfigurationProvider configProvider,
        ILogger<BullyLeaderElectionService>? logger = null,
        TimeSpan? electionInterval = null)
    {
        _discoveryService = discoveryService ?? throw new ArgumentNullException(nameof(discoveryService));
        _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
        _logger = logger ?? NullLogger<BullyLeaderElectionService>.Instance;
        _electionInterval = electionInterval ?? TimeSpan.FromSeconds(5);
    }

    public async Task Start()
    {
        if (_cts != null)
        {
            _logger.LogWarning("Leader election service already started");
            return;
        }

        var config = await _configProvider.GetConfiguration();
        _localNodeId = config.NodeId;

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ElectionLoopAsync(_cts.Token));

        _logger.LogInformation("Leader election service started for node {NodeId}", _localNodeId);
    }

    public Task Stop()
    {
        if (_cts == null) return Task.CompletedTask;

        _cts.Cancel();
        _cts.Dispose();
        _cts = null;

        _logger.LogInformation("Leader election service stopped");
        return Task.CompletedTask;
    }

    private async Task ElectionLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_electionInterval, cancellationToken);
                RunElection();
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during leader election");
            }
        }
    }

    private void RunElection()
    {
        if (_localNodeId == null) return;

        // Get all active LAN peers (excluding remote cloud nodes)
        var lanPeers = _discoveryService.GetActivePeers()
            .Where(p => p.Type == PeerType.LanDiscovered)
            .Select(p => p.NodeId)
            .ToList();

        // Add local node to the pool
        lanPeers.Add(_localNodeId);

        // Bully algorithm: smallest NodeId wins (lexicographic comparison)
        var newLeader = lanPeers.OrderBy(id => id, StringComparer.Ordinal).FirstOrDefault();

        if (newLeader == null)
        {
            // No peers available, local node is leader by default
            newLeader = _localNodeId;
        }

        // Check if leadership changed
        if (newLeader != _currentGatewayNodeId)
        {
            var wasLeader = _isCloudGateway;
            _currentGatewayNodeId = newLeader;
            _isCloudGateway = newLeader == _localNodeId;

            if (wasLeader != _isCloudGateway)
            {
                if (_isCloudGateway)
                {
                    _logger.LogInformation("🔐 This node is now the CLOUD GATEWAY (Leader) - Will sync with remote cloud nodes");
                }
                else
                {
                    _logger.LogInformation("👤 This node is now a MEMBER - Cloud sync handled by gateway: {Gateway}", _currentGatewayNodeId);
                }

                // Raise event
                LeadershipChanged?.Invoke(this, new LeadershipChangedEventArgs(_currentGatewayNodeId, _isCloudGateway));
            }
        }
    }
}
