namespace EntglDb.AspNet.Configuration;

/// <summary>
/// Defines the server operating mode for EntglDb in ASP.NET applications.
/// </summary>
public enum ServerMode
{
    /// <summary>
    /// Single-cluster mode: One EntglDb instance serves one cluster/database.
    /// Best for dedicated database servers or simple deployments.
    /// </summary>
    SingleCluster,

    /// <summary>
    /// Multi-cluster mode: One EntglDb instance serves multiple clusters/databases.
    /// Best for multi-tenant scenarios or shared hosting environments.
    /// </summary>
    MultiCluster
}
