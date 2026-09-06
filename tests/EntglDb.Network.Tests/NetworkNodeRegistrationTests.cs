using EntglDb.Core.Network;
using EntglDb.Network;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EntglDb.Network.Tests;

/// <summary>
/// A consumer that uses EntglDb purely as a peer-to-peer transport registers nothing but
/// <c>AddEntglDbNetwork</c> and <c>AddEntglDbNetworkNode</c>. Everything the node needs has to be
/// resolvable from those two alone.
/// </summary>
/// <remarks>
/// The first release of the transport-only path shipped unable to build its own node: the discovery service
/// and the TCP server both take an <see cref="ILocalInterestsProvider"/>, which until then only the document
/// store registered - so the very consumer this path exists for could not start one. Resolving the graph is
/// the whole assertion.
/// </remarks>
public class NetworkNodeRegistrationTests
{
    private static ServiceProvider BuildTransportOnlyProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddEntglDbNetwork<TestConfigurationProvider>();
        services.AddEntglDbNetworkNode(useHostedService: false);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = false });
    }

    [Fact]
    public void TransportOnlyRegistration_ResolvesTheNode()
    {
        using var provider = BuildTransportOnlyProvider();

        var node = provider.GetRequiredService<INetworkNode>();

        node.Should().NotBeNull();
        node.Server.Should().NotBeNull();
        node.Discovery.Should().NotBeNull();
    }

    [Fact]
    public void TransportOnlyRegistration_ResolvesTheMessengerAndPool()
    {
        using var provider = BuildTransportOnlyProvider();

        provider.GetRequiredService<IPeerMessenger>().Should().NotBeNull();
        provider.GetRequiredService<IPeerConnectionPool>().Should().NotBeNull();
    }

    [Fact]
    public void TransportOnlyRegistration_AdvertisesNoCollectionInterests()
    {
        using var provider = BuildTransportOnlyProvider();

        provider.GetRequiredService<ILocalInterestsProvider>().InterestedCollection.Should().BeEmpty();
    }

    private sealed class TestConfigurationProvider : IPeerNodeConfigurationProvider
    {
        public event PeerNodeConfigurationChangedEventHandler? ConfigurationChanged;

        public Task<PeerNodeConfiguration> GetConfiguration() =>
            Task.FromResult(new PeerNodeConfiguration { NodeId = "test-node" });
    }
}
