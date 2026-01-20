using System;

namespace EntglDb.AspNet.Configuration;

/// <summary>
/// Configuration options for single-cluster mode.
/// </summary>
public class SingleClusterOptions
{
    /// <summary>
    /// Gets or sets the node identifier for this instance.
    /// </summary>
    public string NodeId { get; set; } = Environment.MachineName;

    /// <summary>
    /// Gets or sets the TCP port for sync operations.
    /// Default: 5001
    /// </summary>
    public int TcpPort { get; set; } = 5001;

    /// <summary>
    /// Gets or sets whether to enable UDP discovery.
    /// Default: false (disabled in server mode)
    /// </summary>
    public bool EnableUdpDiscovery { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to enable OAuth2 JWT validation.
    /// When true, requires valid JWT tokens for sync operations.
    /// Default: false
    /// </summary>
    public bool RequireAuthentication { get; set; } = false;

    /// <summary>
    /// Gets or sets the OAuth2 authority URL (e.g., https://auth.example.com).
    /// Required when RequireAuthentication is true.
    /// </summary>
    public string? OAuth2Authority { get; set; }

    /// <summary>
    /// Gets or sets the expected OAuth2 audience claim.
    /// Required when RequireAuthentication is true.
    /// </summary>
    public string? OAuth2Audience { get; set; }
}
