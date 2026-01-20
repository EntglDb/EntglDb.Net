using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Network.Security;

/// <summary>
/// Interface for obtaining OAuth2 access tokens.
/// Implementations should handle token caching and automatic refresh.
/// </summary>
public interface ITokenProvider
{
    /// <summary>
    /// Gets a valid access token, refreshing if necessary.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A valid OAuth2 access token.</returns>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
