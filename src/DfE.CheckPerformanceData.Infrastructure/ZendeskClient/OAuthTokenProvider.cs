using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient;

/// <summary>
/// Provides OAuth access tokens for the Zendesk API using the client credentials flow.
/// Handles automatic token refresh when the access token expires.
///
/// The client credentials flow does NOT issue a refresh token - when the access token
/// expires, a new one is requested using the same client_id/client_secret credentials.
///
/// See: https://developer.zendesk.com/documentation/authentication/oauth-migration/
/// </summary>
public interface IOAuthTokenProvider
{
    /// <summary>
    /// Gets a valid access token, refreshing it if necessary.
    /// </summary>
    Task<string> GetAccessTokenAsync();
}

public sealed class OAuthTokenProvider : IOAuthTokenProvider
{
    private readonly HttpClient _httpClient;
    private readonly ZendeskSettings _settings;
    private readonly ILogger<OAuthTokenProvider> _logger;

    // Track token expiry to avoid unnecessary token requests
    private DateTimeOffset _tokenExpiryUtc;
    private string _currentToken = string.Empty;

    public OAuthTokenProvider(
        IOptions<ZendeskSettings> settings,
        ILogger<OAuthTokenProvider> logger)
        //ZendeskSettings zendeskSettings)
    {
        _settings = settings.Value;
        _logger = logger;
        _httpClient = new HttpClient
        {
            BaseAddress = DependencyManager.ZendeskBaseAddress(_settings)
        };
    }

    public async Task<string> GetAccessTokenAsync()
    {
        // If we have a valid token that hasn't expired (with a 30-second safety margin),
        // return it without making a network call
        // Compare now+30s to expiry to avoid subtracting from a potentially default MinValue expiry
        if (!string.IsNullOrEmpty(_currentToken) && DateTimeOffset.UtcNow.AddSeconds(30) < _tokenExpiryUtc)
        {
            return _currentToken;
        }


        // Token is expired or we don't have one yet - fetch a fresh token
        var tokenResponse = await FetchTokenAsync().ConfigureAwait(false);
        return tokenResponse.AccessToken;
    }

    private async Task<OAuthTokenResponse> FetchTokenAsync()
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", _settings.ClientId),
            new KeyValuePair<string, string>("client_secret", _settings.ClientSecret),
            new KeyValuePair<string, string>("scope", _settings.Scopes)
        });

        _logger.LogInformation("Requesting Zendesk OAuth access token from {BaseAddress}oauth/tokens", 
            _httpClient.BaseAddress);

        var response = await _httpClient.PostAsync("/oauth/tokens", content).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _logger.LogError(
                "Failed to obtain Zendesk OAuth access token. Status: {StatusCode}, Response: {Response}",
                response.StatusCode, errorBody);
            throw new HttpRequestException(
                $"Zendesk OAuth token request failed: {response.StatusCode} - {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Failed to deserialize OAuth token response");

        // Store the token and calculate expiry time
        _currentToken = tokenResponse.AccessToken;
        // Default expiry is 30 minutes if not specified; docs allow 5 min to 48 hours
        var expiresInSeconds = tokenResponse.ExpiresIn > 0 ? tokenResponse.ExpiresIn : 1800;
        _tokenExpiryUtc = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);

        _logger.LogInformation(
            "Obtained Zendesk OAuth access token. Expires in {ExpiresIn}s (at {Expiry:u})",
            expiresInSeconds, _tokenExpiryUtc);

        return tokenResponse;
    }
}