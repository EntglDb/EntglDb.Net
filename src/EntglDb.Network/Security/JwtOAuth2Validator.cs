using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Network.Security;

/// <summary>
/// Simple JWT OAuth2 validator for EntglDB.
/// NOTE: This is a basic implementation for demonstration. 
/// For production use, integrate Microsoft.IdentityModel.Tokens with proper JWKS validation,
/// signature verification, issuer/audience validation, and lifetime checks.
/// </summary>
public class JwtOAuth2Validator : IOAuth2Validator
{
    private readonly ILogger<JwtOAuth2Validator> _logger;
    private readonly string? _expectedIssuer;
    private readonly string? _expectedAudience;

    /// <summary>
    /// Initializes a new instance of the JwtOAuth2Validator class.
    /// </summary>
    /// <param name="expectedIssuer">Expected issuer claim (optional for basic validation).</param>
    /// <param name="expectedAudience">Expected audience claim (optional for basic validation).</param>
    /// <param name="logger">Logger instance.</param>
    public JwtOAuth2Validator(
        string? expectedIssuer = null,
        string? expectedAudience = null,
        ILogger<JwtOAuth2Validator>? logger = null)
    {
        _expectedIssuer = expectedIssuer;
        _expectedAudience = expectedAudience;
        _logger = logger ?? NullLogger<JwtOAuth2Validator>.Instance;
    }

    public Task<OAuth2ValidationResult> ValidateTokenAsync(string jwtToken, CancellationToken cancellationToken = default)
    {
        try
        {
            // Basic JWT structure validation (header.payload.signature)
            var parts = jwtToken.Split('.');
            if (parts.Length != 3)
            {
                return Task.FromResult(new OAuth2ValidationResult
                {
                    IsValid = false,
                    Error = "Invalid JWT structure - expected 3 parts separated by dots"
                });
            }

            // Decode payload (middle part)
            var payload = DecodeBase64Url(parts[1]);
            var claims = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payload);

            if (claims == null)
            {
                return Task.FromResult(new OAuth2ValidationResult
                {
                    IsValid = false,
                    Error = "Failed to parse JWT payload"
                });
            }

            // Extract claims as strings
            var claimsDict = new Dictionary<string, string>();
            foreach (var claim in claims)
            {
                claimsDict[claim.Key] = claim.Value.ToString();
            }

            // Validate issuer if configured
            if (!string.IsNullOrEmpty(_expectedIssuer))
            {
                if (!claims.TryGetValue("iss", out var issuer) || issuer.GetString() != _expectedIssuer)
                {
                    return Task.FromResult(new OAuth2ValidationResult
                    {
                        IsValid = false,
                        Error = $"Invalid issuer. Expected: {_expectedIssuer}"
                    });
                }
            }

            // Validate audience if configured
            if (!string.IsNullOrEmpty(_expectedAudience))
            {
                if (!claims.TryGetValue("aud", out var audience) || audience.GetString() != _expectedAudience)
                {
                    return Task.FromResult(new OAuth2ValidationResult
                    {
                        IsValid = false,
                        Error = $"Invalid audience. Expected: {_expectedAudience}"
                    });
                }
            }

            // Validate expiration
            if (claims.TryGetValue("exp", out var exp))
            {
                var expirationTime = DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64());
                if (expirationTime < DateTimeOffset.UtcNow)
                {
                    return Task.FromResult(new OAuth2ValidationResult
                    {
                        IsValid = false,
                        Error = "Token has expired"
                    });
                }
            }

            // Validate not-before if present
            if (claims.TryGetValue("nbf", out var nbf))
            {
                var notBeforeTime = DateTimeOffset.FromUnixTimeSeconds(nbf.GetInt64());
                if (notBeforeTime > DateTimeOffset.UtcNow)
                {
                    return Task.FromResult(new OAuth2ValidationResult
                    {
                        IsValid = false,
                        Error = "Token is not yet valid"
                    });
                }
            }

            _logger.LogInformation("JWT token validated successfully");

            return Task.FromResult(new OAuth2ValidationResult
            {
                IsValid = true,
                Claims = claimsDict
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating JWT token");
            return Task.FromResult(new OAuth2ValidationResult
            {
                IsValid = false,
                Error = $"Validation error: {ex.Message}"
            });
        }
    }

    private static string DecodeBase64Url(string base64Url)
    {
        // Convert base64url to standard base64
        var base64 = base64Url.Replace('-', '+').Replace('_', '/');

        // Add padding if needed
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }

        var bytes = Convert.FromBase64String(base64);
        return Encoding.UTF8.GetString(bytes);
    }
}
