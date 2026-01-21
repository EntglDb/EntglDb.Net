namespace EntglDb.AspNet.Configuration;

/// <summary>
/// Configuration options for EntglDb ASP.NET integration.
/// </summary>
public class EntglDbAspNetOptions
{
    /// <summary>
    /// Gets or sets the server operating mode.
    /// Default: SingleCluster
    /// </summary>
    public ServerMode Mode { get; set; } = ServerMode.SingleCluster;

    /// <summary>
    /// Gets or sets the single-cluster configuration.
    /// Used when Mode is SingleCluster.
    /// </summary>
    public SingleClusterOptions SingleCluster { get; set; } = new();

    /// <summary>
    /// Gets or sets the multi-cluster configuration.
    /// Used when Mode is MultiCluster.
    /// </summary>
    public MultiClusterOptions MultiCluster { get; set; } = new();

    /// <summary>
    /// Gets or sets whether to enable health checks.
    /// Default: true
    /// </summary>
    public bool EnableHealthChecks { get; set; } = true;
}
