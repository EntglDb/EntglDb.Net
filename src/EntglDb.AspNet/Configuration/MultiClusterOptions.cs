namespace EntglDb.AspNet.Configuration;

/// <summary>
/// Configuration options for multi-cluster mode.
/// </summary>
public class MultiClusterOptions
{
    /// <summary>
    /// Gets or sets the base port for cluster routing.
    /// Each cluster gets a port starting from this base.
    /// Default: 5001
    /// </summary>
    public int BasePort { get; set; } = 5001;

    /// <summary>
    /// Gets or sets the number of clusters to serve.
    /// Default: 1
    /// </summary>
    public int ClusterCount { get; set; } = 1;

    /// <summary>
    /// Gets or sets whether to enable OAuth2 JWT validation.
    /// When true, requires valid JWT tokens for sync operations.
    /// Default: true (recommended for multi-tenant scenarios)
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
    /// Gets or sets the node ID template.
    /// Use {ClusterId} as placeholder (e.g., "server-{ClusterId}").
    /// Default: "{ClusterId}"
    /// </summary>
    public string NodeIdTemplate { get; set; } = "{ClusterId}";
}
