using System.Net.Http.Headers;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient;

/// <summary>
/// HTTP message handler that injects a Zendesk OAuth Bearer token into outgoing requests.
/// Uses IOAuthTokenProvider to obtain and cache tokens, refreshing them automatically.
/// </summary>
public sealed class ZendeskOAuthHandler : DelegatingHandler
{
    private readonly IOAuthTokenProvider _tokenProvider;

    public ZendeskOAuthHandler(IOAuthTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        System.Threading.CancellationToken cancellationToken)
    {
        // Get a valid (non-expired) access token, refreshing if needed
        var accessToken = await _tokenProvider.GetAccessTokenAsync().ConfigureAwait(false);

        // Remove any existing authorization header to avoid conflicts
        request.Headers.Authorization = null;

        // Set the Bearer token for OAuth authentication
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}