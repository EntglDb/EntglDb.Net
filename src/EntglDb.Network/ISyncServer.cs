using System.Net;
using System.Threading.Tasks;

namespace EntglDb.Network;

/// <summary>
/// Defines the contract for a server that supports starting, stopping, and reporting its listening network endpoint for
/// synchronization operations.
/// </summary>
/// <remarks>Implementations of this interface are expected to provide asynchronous methods for starting and
/// stopping the server. The listening endpoint may be null if the server is not currently active or has not been
/// started.</remarks>
public interface ISyncServer
{
    Task Start();

    Task Stop();

    IPEndPoint? ListeningEndpoint { get; }
}