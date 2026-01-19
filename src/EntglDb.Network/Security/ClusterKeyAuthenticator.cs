using EntglDb.Core.Network;
using System.Threading.Tasks;

namespace EntglDb.Network.Security;

/// <summary>
/// Authenticator implementation that uses a shared secret (pre-shared key) to validate nodes.
/// Both nodes must possess the same key to successfully handshake.
/// </summary>
public class ClusterKeyAuthenticator : IAuthenticator
{
    private readonly IPeerNodeConfigurationProvider _peerNodeConfigurationProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClusterKeyAuthenticator"/> class.
    /// </summary>
    /// <param name="peerNodeConfigurationProvider">The provider for peer node configuration.</param>
    public ClusterKeyAuthenticator(IPeerNodeConfigurationProvider peerNodeConfigurationProvider)
    {
        _peerNodeConfigurationProvider = peerNodeConfigurationProvider;
    }

    /// <inheritdoc />
    public async Task<bool> ValidateAsync(string nodeId, string token)
    {
        var config = await _peerNodeConfigurationProvider.GetConfiguration();
        return config.AuthToken == token;
    }
}
