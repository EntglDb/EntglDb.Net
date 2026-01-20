using System;

namespace EntglDb.AspNet.Configuration;

/// <summary>
/// Configuration options for multi-cluster mode.
/// In multi-cluster mode, tenant information is provided by an ITenantService implementation.
/// </summary>
public class MultiClusterOptions
{
    /// <summary>
    /// Gets or sets the TCP port for sync operations.
    /// All tenants/clusters share this single port.
    /// Default: 5001
    /// </summary>
    public int TcpPort { get; set; } = 5001;

    /// <summary>
    /// Gets or sets the maximum number of concurrent tenant nodes that can be active.
    /// This limits resource usage. Set to 0 for unlimited.
    /// Default: 100
    /// </summary>
    public int MaxConcurrentTenants { get; set; } = 100;

    /// <summary>
    /// Gets or sets the interval (in seconds) for refreshing tenant information from the tenant service.
    /// Default: 300 seconds (5 minutes)
    /// </summary>
    public int TenantRefreshIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets whether to enable OAuth2 JWT validation.
    /// When true, requires valid JWT tokens for sync operations.
    /// The JWT must contain a cluster_id claim to identify the tenant.
    /// Default: true (required for multi-tenant scenarios)
    /// </summary>
    public bool RequireAuthentication { get; set; } = true;

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

    /// <summary>
    /// Gets or sets the JWT claim name that contains the cluster/tenant identifier.
    /// Default: "cluster_id"
    /// </summary>
    public string ClusterIdClaimName { get; set; } = "cluster_id";

    /// <summary>
    /// Gets or sets the node ID template.
    /// Use {ClusterId} as placeholder (e.g., "server-{ClusterId}").
    /// Default: "cloud-{ClusterId}"
    /// </summary>
    public string NodeIdTemplate { get; set; } = "cloud-{ClusterId}";
}
