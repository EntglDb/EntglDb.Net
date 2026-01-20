using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Network.Security;
using EntglDb.Core.Network;

/// <summary>
/// Token provider implementing OAuth2 Client Credentials flow.
/// Caches tokens and automatically refreshes before expiration.
/// </summary>
public class OAuth2ClientCredentialsTokenProvider : ITokenProvider, IDisposable
{
    private readonly OAuth2Configuration _config;
    private readonly HttpClient _httpClient;
    private readonly ILogger<OAuth2ClientCredentialsTokenProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _tokenExpiration = DateTimeOffset.MinValue;

    /// <summary>
    /// Initializes a new instance of the OAuth2ClientCredentialsTokenProvider class.
    /// </summary>
    /// <param name="config">OAuth2 configuration.</param>
    /// <param name="httpClient">HTTP client for making token requests. If null, a new instance is created.</param>
    /// <param name="logger">Logger instance.</param>
    public OAuth2ClientCredentialsTokenProvider(
        OAuth2Configuration config,
        HttpClient? httpClient = null,
        ILogger<OAuth2ClientCredentialsTokenProvider>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClient = httpClient ?? new HttpClient();
        _logger = logger ?? NullLogger<OAuth2ClientCredentialsTokenProvider>.Instance;

        ValidateConfiguration();
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        // Check if cached token is still valid (with 60 second buffer for safety)
        if (!string.IsNullOrEmpty(_cachedToken) && _tokenExpiration > DateTimeOffset.UtcNow.AddSeconds(60))
        {
            return _cachedToken;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (!string.IsNullOrEmpty(_cachedToken) && _tokenExpiration > DateTimeOffset.UtcNow.AddSeconds(60))
            {
                return _cachedToken;
            }

            // Request new token
            _logger.LogInformation("Requesting new OAuth2 access token from {Authority}", _config.Authority);
            var token = await RequestTokenAsync(cancellationToken);
            
            _cachedToken = token.AccessToken;
            _tokenExpiration = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);

            _logger.LogInformation("OAuth2 access token acquired, expires at {Expiration}", _tokenExpiration);
            return _cachedToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<TokenResponse> RequestTokenAsync(CancellationToken cancellationToken)
    {
        var tokenEndpoint = $"{_config.Authority.TrimEnd('/')}/connect/token";

        var requestBody = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _config.ClientId,
            ["client_secret"] = _config.ClientSecret
        };

        if (_config.Scopes.Any())
        {
            requestBody["scope"] = string.Join(" ", _config.Scopes);
        }

        if (!string.IsNullOrEmpty(_config.Audience))
        {
            requestBody["audience"] = _config.Audience;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(requestBody)
        };

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to obtain OAuth2 token: {response.StatusCode} - {error}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(json);

        if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
        {
            throw new InvalidOperationException("Invalid token response from OAuth2 server");
        }

        return tokenResponse;
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_config.Authority))
            throw new ArgumentException("OAuth2 Authority is required", nameof(_config));
        
        if (string.IsNullOrWhiteSpace(_config.ClientId))
            throw new ArgumentException("OAuth2 ClientId is required", nameof(_config));
        
        if (string.IsNullOrWhiteSpace(_config.ClientSecret))
            throw new ArgumentException("OAuth2 ClientSecret is required", nameof(_config));
    }

    public void Dispose()
    {
        _lock?.Dispose();
    }

    private class TokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("token_type")]
        public string TokenType { get; set; } = "Bearer";
    }
}
