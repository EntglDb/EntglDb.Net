using System;

namespace EntglDb.Core.Network;

/// <summary>
/// Configuration for OAuth2 authentication.
/// </summary>
public class OAuth2Configuration
{
    /// <summary>
    /// Gets or sets the OAuth2 authority URL (e.g., https://identity.example.com).
    /// </summary>
    public string Authority { get; set; } = "";

    /// <summary>
    /// Gets or sets the client ID for OAuth2 client credentials flow.
    /// </summary>
    public string ClientId { get; set; } = "";

    /// <summary>
    /// Gets or sets the client secret for OAuth2 client credentials flow.
    /// </summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// Gets or sets the scopes to request (e.g., "entgldb:sync").
    /// </summary>
    public string[] Scopes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the audience for the token (optional).
    /// </summary>
    public string? Audience { get; set; }
}
