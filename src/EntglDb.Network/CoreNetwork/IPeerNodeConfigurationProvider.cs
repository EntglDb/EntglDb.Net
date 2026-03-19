using System;
using System.Threading.Tasks;

namespace EntglDb.Core.Network;

public delegate void PeerNodeConfigurationChangedEventHandler(object? sender, PeerNodeConfiguration newConfig);

/// <summary>
/// Defines a contract for retrieving and monitoring configuration settings for a peer node.
/// </summary>
/// <remarks>Implementations of this interface provide access to the current configuration and notify subscribers
/// when configuration changes occur. This interface is typically used by components that require up-to-date
/// configuration information for peer-to-peer networking scenarios.</remarks>
public interface IPeerNodeConfigurationProvider
{
    /// <summary>
    /// Asynchronously retrieves the current configuration settings for the peer node.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains a <see
    /// cref="PeerNodeConfiguration"/> object with the current configuration settings.</returns>
    public Task<PeerNodeConfiguration> GetConfiguration();

    /// <summary>
    /// Occurs when the configuration of the peer node changes.
    /// </summary>
    /// <remarks>Subscribe to this event to be notified when any configuration settings for the peer node are
    /// modified. Event handlers can use this notification to update dependent components or respond to configuration
    /// changes as needed.</remarks>

    public event PeerNodeConfigurationChangedEventHandler? ConfigurationChanged;
}
