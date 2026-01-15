using System.Threading.Tasks;

namespace EntglDb.Network.Security
{
    /// <summary>
    /// Authenticator implementation that uses a shared secret (pre-shared key) to validate nodes.
    /// Both nodes must possess the same key to successfully handshake.
    /// </summary>
    public class ClusterKeyAuthenticator : IAuthenticator
    {
        private readonly string _sharedKey;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterKeyAuthenticator"/> class.
        /// </summary>
        /// <param name="sharedKey">The secret key shared among trusted peers.</param>
        public ClusterKeyAuthenticator(string sharedKey)
        {
            _sharedKey = sharedKey;
        }

        /// <inheritdoc />
        public Task<bool> ValidateAsync(string nodeId, string token)
        {
            return Task.FromResult(token == _sharedKey);
        }
    }
}
