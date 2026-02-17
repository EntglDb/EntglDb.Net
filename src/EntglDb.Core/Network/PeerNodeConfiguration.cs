using System;

namespace EntglDb.Core.Network;

/// <summary>
/// Represents the configuration settings for a peer node in a distributed network.
/// </summary>
/// <remarks>Use this class to specify identification, network port, and authentication details required for a
/// peer node to participate in a cluster or peer-to-peer environment. The <see cref="Default"/> property provides a
/// basic configuration suitable for development or testing scenarios.</remarks>
public class PeerNodeConfiguration
{
    /// <summary>
    /// Gets or sets the unique identifier for the node.
    /// </summary>
    public string NodeId { get; set; }

    /// <summary>
    /// Gets or sets the TCP port number used for network communication.
    /// </summary>
    public int TcpPort { get; set; }

    /// <summary>
    /// Gets or sets the authentication token used to authorize API requests.
    /// </summary>
    public string AuthToken { get; set; }

    /// <summary>
    /// Maximum size of the document cache items. Default: 10.
    /// </summary>
    public int MaxDocumentCacheSize { get; set; } = 100;

    /// <summary>
    /// Maximum size of offline queue. Default: 1000.
    /// </summary>
    public int MaxQueueSize { get; set; } = 1000;

    /// <summary>
    /// Number of retry attempts for failed network operations. Default: 3.
    /// </summary>
    public int RetryAttempts { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts in milliseconds. Default: 1000ms.
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// Interval between periodic maintenance operations (Oplog pruning) in minutes. Default: 60 minutes.
    /// </summary>
    public int MaintenanceIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Oplog retention period in hours. Entries older than this will be pruned. Default: 24 hours.
    /// </summary>
    public int OplogRetentionHours { get; set; } = 24;

    /// <summary>
    /// Gets or sets a list of known peers to connect to directly, bypassing discovery.
    /// </summary>
    public System.Collections.Generic.List<KnownPeerConfiguration> KnownPeers { get; set; } = new();

    /// <summary>
    /// Gets the default configuration settings for a peer node.
    /// </summary>
    /// <remarks>Each access returns a new instance of the configuration with a unique node identifier. The
    /// default settings use TCP port 9000 and a predefined authentication token. Modify the returned instance as needed
    /// before use.</remarks>
    public static PeerNodeConfiguration Default => new PeerNodeConfiguration
    {
        NodeId = Guid.NewGuid().ToString(),
        TcpPort = 9000,
        AuthToken = "default-cluster-token"
    };
}

/// <summary>
/// Configuration for a known peer node.
/// </summary>
public class KnownPeerConfiguration
{
    /// <summary>
    /// The unique identifier of the peer node.
    /// </summary>
    public string NodeId { get; set; }

    /// <summary>
    /// The hostname or IP address of the peer.
    /// </summary>
    public string Host { get; set; }

    /// <summary>
    /// The TCP port of the peer.
    /// </summary>
    public int Port { get; set; }
}