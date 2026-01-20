using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.AspNet.Services;

/// <summary>
/// Information about a tenant/cluster.
/// </summary>
public class TenantInfo
{
    /// <summary>
    /// Gets or sets the unique identifier for the tenant/cluster.
    /// </summary>
    public string ClusterId { get; set; } = "";

    /// <summary>
    /// Gets or sets the authentication token (cluster-key) for this tenant.
    /// Used to validate that peers belong to this specific cluster.
    /// </summary>
    public string AuthToken { get; set; } = "";

    /// <summary>
    /// Gets or sets whether this tenant is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets the database connection string for this tenant (optional).
    /// If not provided, a shared database with tenant isolation may be used.
    /// </summary>
    public string? DatabaseConnectionString { get; set; }
}

/// <summary>
/// Service for providing tenant/cluster information in multi-cluster mode.
/// Implement this interface to integrate with your tenant management system.
/// </summary>
public interface ITenantService
{
    /// <summary>
    /// Gets the list of all active tenants.
    /// This method is called periodically to discover new tenants and retire inactive ones.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of tenant information.</returns>
    Task<IEnumerable<TenantInfo>> GetActiveTenantsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets tenant information by cluster ID.
    /// </summary>
    /// <param name="clusterId">The cluster/tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tenant information, or null if not found.</returns>
    Task<TenantInfo?> GetTenantByClusterIdAsync(string clusterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that the provided authentication token matches the tenant's cluster-key.
    /// </summary>
    /// <param name="clusterId">The cluster/tenant identifier.</param>
    /// <param name="authToken">The authentication token to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the token is valid for the tenant, false otherwise.</returns>
    Task<bool> ValidateAuthTokenAsync(string clusterId, string authToken, CancellationToken cancellationToken = default);
}
