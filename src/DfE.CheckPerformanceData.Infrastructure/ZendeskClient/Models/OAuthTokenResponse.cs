using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models;

/// <summary>
/// Response from the Zendesk OAuth /oauth/tokens endpoint for the client credentials flow.
/// This flow does NOT issue a refresh_token - when the access token expires,
/// the client simply requests a new one using the same credentials.
///
/// See: https://developer.zendesk.com/documentation/authentication/oauth-migration/
/// </summary>
public sealed class OAuthTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;
}