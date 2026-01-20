using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Network;
using EntglDb.Network.Leadership;
using FluentAssertions;
using Xunit;

namespace EntglDb.Network.Tests;

public class BullyLeaderElectionServiceTests
{
    private class MockDiscoveryService : IDiscoveryService
    {
        public List<PeerNode> Peers { get; set; } = new();

        public IEnumerable<PeerNode> GetActivePeers() => Peers;

        public Task Start() => Task.CompletedTask;

        public Task Stop() => Task.CompletedTask;
    }

    private class MockConfigProvider : IPeerNodeConfigurationProvider
    {
        public PeerNodeConfiguration Config { get; set; } = new PeerNodeConfiguration { NodeId = "local-node" };

#pragma warning disable CS0067 // Event never used - OK for mock
        public event PeerNodeConfigurationChangedEventHandler? ConfigurationChanged;
#pragma warning restore CS0067

        public Task<PeerNodeConfiguration> GetConfiguration() => Task.FromResult(Config);
    }

    [Fact]
    public async Task SingleNode_ShouldBecomeLeader()
    {
        // Arrange
        var discoveryService = new MockDiscoveryService();
        var configProvider = new MockConfigProvider { Config = new PeerNodeConfiguration { NodeId = "node-A" } };
        var electionService = new BullyLeaderElectionService(discoveryService, configProvider, electionInterval: TimeSpan.FromMilliseconds(100));

        LeadershipChangedEventArgs? lastEvent = null;
        electionService.LeadershipChanged += (_, e) => lastEvent = e;

        // Act
        await electionService.Start();
        await Task.Delay(200); // Wait for first election

        // Assert
        electionService.IsCloudGateway.Should().BeTrue();
        electionService.CurrentGatewayNodeId.Should().Be("node-A");
        lastEvent.Should().NotBeNull();
        lastEvent!.IsLocalNodeGateway.Should().BeTrue();
        lastEvent.CurrentGatewayNodeId.Should().Be("node-A");

        await electionService.Stop();
    }

    [Fact]
    public async Task MultipleNodes_SmallestNodeIdShouldBeLeader()
    {
        // Arrange
        var discoveryService = new MockDiscoveryService
        {
            Peers = new List<PeerNode>
            {
                new PeerNode("node-B", "192.168.1.2:9000", DateTimeOffset.UtcNow, PeerType.LanDiscovered),
                new PeerNode("node-C", "192.168.1.3:9000", DateTimeOffset.UtcNow, PeerType.LanDiscovered)
            }
        };

        var configProvider = new MockConfigProvider { Config = new PeerNodeConfiguration { NodeId = "node-A" } };
        var electionService = new BullyLeaderElectionService(discoveryService, configProvider, electionInterval: TimeSpan.FromMilliseconds(100));

        // Act
        await electionService.Start();
        await Task.Delay(200); // Wait for first election

        // Assert - node-A is smallest lexicographically
        electionService.IsCloudGateway.Should().BeTrue();
        electionService.CurrentGatewayNodeId.Should().Be("node-A");

        await electionService.Stop();
    }

    [Fact]
    public async Task LocalNodeNotSmallest_ShouldNotBeLeader()
    {
        // Arrange
        var discoveryService = new MockDiscoveryService
        {
            Peers = new List<PeerNode>
            {
                new PeerNode("node-A", "192.168.1.1:9000", DateTimeOffset.UtcNow, PeerType.LanDiscovered),
                new PeerNode("node-B", "192.168.1.2:9000", DateTimeOffset.UtcNow, PeerType.LanDiscovered)
            }
        };

        var configProvider = new MockConfigProvider { Config = new PeerNodeConfiguration { NodeId = "node-C" } };
        var electionService = new BullyLeaderElectionService(discoveryService, configProvider, electionInterval: TimeSpan.FromMilliseconds(100));

        // Act
        await electionService.Start();
        await Task.Delay(200); // Wait for first election

        // Assert - node-A is smallest, not node-C
        electionService.IsCloudGateway.Should().BeFalse();
        electionService.CurrentGatewayNodeId.Should().Be("node-A");

        await electionService.Stop();
    }

    [Fact]
    public async Task LeaderFailure_ShouldReelect()
    {
        // Arrange
        var discoveryService = new MockDiscoveryService
        {
            Peers = new List<PeerNode>
            {
                new PeerNode("node-A", "192.168.1.1:9000", DateTimeOffset.UtcNow, PeerType.LanDiscovered)
            }
        };

        var configProvider = new MockConfigProvider { Config = new PeerNodeConfiguration { NodeId = "node-B" } };
        var electionService = new BullyLeaderElectionService(discoveryService, configProvider, electionInterval: TimeSpan.FromMilliseconds(100));

        var leadershipChanges = new List<LeadershipChangedEventArgs>();
        electionService.LeadershipChanged += (_, e) => leadershipChanges.Add(e);

        // Act
        await electionService.Start();
        await Task.Delay(200); // First election: node-A is leader

        electionService.CurrentGatewayNodeId.Should().Be("node-A");

        // Simulate node-A failure
        discoveryService.Peers.Clear();
        await Task.Delay(200); // Re-election: node-B becomes leader

        // Assert
        electionService.IsCloudGateway.Should().BeTrue();
        electionService.CurrentGatewayNodeId.Should().Be("node-B");
        
        // Should have at least 2 leadership changes: first to node-A, then to node-B
        // Or might be just 1 if only the change to node-B was recorded
        leadershipChanges.Should().NotBeEmpty();
        leadershipChanges.Last().IsLocalNodeGateway.Should().BeTrue();
        leadershipChanges.Last().CurrentGatewayNodeId.Should().Be("node-B");

        await electionService.Stop();
    }

    [Fact]
    public async Task CloudPeersExcludedFromElection()
    {
        // Arrange - Include a cloud peer that should be ignored
        var discoveryService = new MockDiscoveryService
        {
            Peers = new List<PeerNode>
            {
                new PeerNode("node-A", "192.168.1.1:9000", DateTimeOffset.UtcNow, PeerType.LanDiscovered),
                new PeerNode("cloud-node-Z", "cloud.example.com:9000", DateTimeOffset.UtcNow, PeerType.CloudRemote) // Should be excluded
            }
        };

        var configProvider = new MockConfigProvider { Config = new PeerNodeConfiguration { NodeId = "node-B" } };
        var electionService = new BullyLeaderElectionService(discoveryService, configProvider, electionInterval: TimeSpan.FromMilliseconds(100));

        // Act
        await electionService.Start();
        await Task.Delay(200);

        // Assert - node-A should be leader (cloud-node-Z excluded from election)
        electionService.CurrentGatewayNodeId.Should().Be("node-A");

        await electionService.Stop();
    }
}
