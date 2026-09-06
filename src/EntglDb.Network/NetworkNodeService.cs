using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EntglDb.Network
{
    /// <summary>
    /// Starts and stops the <see cref="INetworkNode"/> with the application.
    /// </summary>
    /// <remarks>
    /// Awaits <see cref="INetworkNode.Start"/> rather than launching it fire-and-forget: a node that fails to
    /// start - a port already taken, a missing handler dependency - has to surface as a startup failure. Left
    /// unobserved it produces no listening port, no log and no crash, and the first symptom is silence.
    /// </remarks>
    public class NetworkNodeService : IHostedService
    {
        private readonly INetworkNode _node;
        private readonly ILogger<NetworkNodeService> _logger;

        public NetworkNodeService(INetworkNode node, ILogger<NetworkNodeService> logger)
        {
            _node = node;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken) => _node.Start();

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _node.Stop().ConfigureAwait(false);
            }
            catch (System.Exception ex)
            {
                // Shutdown must not be blocked by a node that is already half down.
                _logger.LogWarning(ex, "Network node did not stop cleanly.");
            }
        }
    }
}
