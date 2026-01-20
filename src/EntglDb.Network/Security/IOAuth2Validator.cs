using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EntglDb.Network.Security;

/// <summary>
/// Result of OAuth2 token validation.
/// </summary>
public class OAuth2ValidationResult
{
    /// <summary>
    /// Gets whether the token is valid.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Gets the claims extracted from the token.
    /// </summary>
    public Dictionary<string, string> Claims { get; set; } = new();

    /// <summary>
    /// Gets the error message if validation failed.
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Interface for validating OAuth2 JWT tokens.
/// Implementations should verify token signature, issuer, audience, and lifetime.
/// </summary>
public interface IOAuth2Validator
{
    /// <summary>
    /// Validates a JWT token and extracts claims.
    /// </summary>
    /// <param name="jwtToken">The JWT token to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with claims if successful.</returns>
    Task<OAuth2ValidationResult> ValidateTokenAsync(string jwtToken, CancellationToken cancellationToken = default);
}
