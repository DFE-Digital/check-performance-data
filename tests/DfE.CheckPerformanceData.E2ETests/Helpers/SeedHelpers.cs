using System.Net;
using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

public static class SeedHelpers
{
    // Saves a new value over an existing content block key (POST /content-block/save),
    // which appends a row to ContentBlockVersions.
    public static async Task EditContentBlockAsync(
        HttpClient client,
        string key,
        string newValue)
    {
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/dev/antiforgery-token");

        var formFields = new[]
        {
            new KeyValuePair<string, string>("Key", key),
            new KeyValuePair<string, string>("BlockType", "Content"),
            new KeyValuePair<string, string>("Value", newValue),
            new KeyValuePair<string, string>("OriginalValue", string.Empty),
            new KeyValuePair<string, string>("ReturnUrl", "/"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/content-block/save")
        {
            Content = new FormUrlEncodedContent(formFields)
        };
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("X-XSRF-TOKEN", token);

        var response = await SendWithoutFollowingRedirects(client, request);

        if (response.StatusCode != HttpStatusCode.Found && response.StatusCode != HttpStatusCode.Redirect)
        {
            response.EnsureSuccessStatusCode();
            throw new InvalidOperationException(
                $"Editing content block '{key}' returned {(int)response.StatusCode} {response.StatusCode}; expected 302.");
        }
    }

    public static async Task<string> SeedContentBlockAsync(
        HttpClient client,
        string keyPrefix,
        string value)
    {
        var key = $"e2e-{Guid.NewGuid():N}-{keyPrefix}";

        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/dev/antiforgery-token");

        var formFields = new[]
        {
            new KeyValuePair<string, string>("Key", key),
            new KeyValuePair<string, string>("BlockType", "Content"),
            new KeyValuePair<string, string>("Value", value),
            new KeyValuePair<string, string>("OriginalValue", string.Empty),
            new KeyValuePair<string, string>("ReturnUrl", "/"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/content-block/save")
        {
            Content = new FormUrlEncodedContent(formFields)
        };
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("X-XSRF-TOKEN", token);

        var response = await SendWithoutFollowingRedirects(client, request);

        if (response.StatusCode != HttpStatusCode.Found && response.StatusCode != HttpStatusCode.Redirect)
        {
            response.EnsureSuccessStatusCode();
            throw new InvalidOperationException(
                $"Seeding content block '{key}' returned {(int)response.StatusCode} {response.StatusCode}; expected 302.");
        }

        return key;
    }

    // Seeds a single dead-lettered message via the dev-only queue seed endpoint and returns
    // its id so a queue-admin test can act on it (redrive/purge). The endpoint enqueues,
    // dequeues and dead-letters in one hop; it 404s in Production.
    public static async Task<Guid> SeedDeadLetterAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/dev/queues/seed-dlq");
        var response = await SendWithoutFollowingRedirects(client, request);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var match = DlqSeedIdPattern.Match(body);
        if (!match.Success || !Guid.TryParse(match.Groups["id"].Value, out var id))
        {
            throw new InvalidOperationException(
                $"Could not parse seeded dead-letter id from response: {body}");
        }

        return id;
    }

    private static readonly Regex DlqSeedIdPattern =
        new("\"id\"\\s*:\\s*\"(?<id>[0-9a-fA-F-]{36})\"", RegexOptions.Compiled);

    private static Task<HttpResponseMessage> SendWithoutFollowingRedirects(
        HttpClient client,
        HttpRequestMessage request)
    {
        // TestHttpClients.NoRedirect has no BaseAddress; resolve relative request URIs
        // against the caller's client so seed POSTs continue to be written as
        // "/content-block/save" etc.
        if (request.RequestUri is { IsAbsoluteUri: false } && client.BaseAddress is not null)
        {
            request.RequestUri = new Uri(client.BaseAddress, request.RequestUri);
        }

        return TestHttpClients.SendAsync(request);
    }
}
