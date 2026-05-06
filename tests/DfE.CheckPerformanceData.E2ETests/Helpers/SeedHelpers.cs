using System.Net;
using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

public static class SeedHelpers
{
    // Pulls the integer Id from the rendered help tree by matching the slug-bearing anchor
    // immediately following a data-page-id="..." attribute in _WikiTree.cshtml. Slugs always
    // include the e2e-{Guid:N}- prefix so collisions with the seeded corpus are not possible.
    private static readonly Regex SlugToIdPattern =
        new(
            "data-page-id=\"(?<id>\\d+)\"[^>]*>\\s*(?:<[^>]+>\\s*)*<a[^>]+href=\"/help/(?<slug>[^\"?]+)",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    public static async Task<int> SeedWikiPageAsync(
        HttpClient client,
        string title,
        string body,
        int? parentId,
        ICollection<int> tracking)
    {
        var (id, _) = await SeedWikiPageReturningSlugAsync(client, title, body, parentId, tracking);
        return id;
    }

    public static async Task<(int Id, string Slug)> SeedWikiPageReturningSlugAsync(
        HttpClient client,
        string title,
        string body,
        int? parentId,
        ICollection<int> tracking)
    {
        var slugPrefix = $"e2e-{Guid.NewGuid():N}";
        var prefixedTitle = $"{slugPrefix} {title}";

        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/help?edit");

        var formFields = new List<KeyValuePair<string, string>>
        {
            new("Title", prefixedTitle),
            new("Content", body),
            new("editMode", "true"),
            new("__RequestVerificationToken", token)
        };

        if (parentId.HasValue)
        {
            formFields.Add(new KeyValuePair<string, string>("ParentId", parentId.Value.ToString()));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/help/create")
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
                $"Seeding wiki page '{prefixedTitle}' returned {(int)response.StatusCode} {response.StatusCode}; expected 302.");
        }

        var location = response.Headers.Location?.ToString()
            ?? throw new InvalidOperationException("POST /help/create did not return a Location header.");

        // Location is /help/{slugPath}{?edit}. Strip the query and the leading "/help/".
        var slugPath = location;
        var queryIndex = slugPath.IndexOf('?');
        if (queryIndex >= 0)
        {
            slugPath = slugPath[..queryIndex];
        }

        const string prefix = "/help/";
        if (!slugPath.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unexpected redirect target '{location}' from POST /help/create.");
        }

        var seededSlug = slugPath[prefix.Length..];

        var id = await ResolveIdFromTreeAsync(client, seededSlug);
        tracking.Add(id);
        return (id, seededSlug);
    }

    public static async Task<string> SeedContentBlockAsync(
        HttpClient client,
        string keyPrefix,
        string value)
    {
        var key = $"e2e-{Guid.NewGuid():N}-{keyPrefix}";

        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/help?edit");

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

    public static async Task SoftDeleteWikiPageAsync(HttpClient client, int id)
    {
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/help?edit");

        var formFields = new[]
        {
            new KeyValuePair<string, string>("editMode", "true"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/help/delete/{id}")
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
                $"Soft-deleting wiki page {id} returned {(int)response.StatusCode} {response.StatusCode}; expected 302.");
        }
    }

    internal static async Task<int> ResolveIdFromTreeAsync(HttpClient client, string slugPath)
    {
        var response = await client.GetAsync("/help");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        foreach (Match match in SlugToIdPattern.Matches(html))
        {
            var renderedSlug = match.Groups["slug"].Value;
            if (string.Equals(renderedSlug, slugPath, StringComparison.Ordinal)
                && int.TryParse(match.Groups["id"].Value, out var id))
            {
                return id;
            }
        }

        throw new InvalidOperationException(
            $"Could not resolve wiki page Id for slug '{slugPath}' from the rendered tree at /help.");
    }

    private static async Task<HttpResponseMessage> SendWithoutFollowingRedirects(
        HttpClient client,
        HttpRequestMessage request)
    {
        // The shared SeedClient follows redirects; clone the request onto a transient client
        // configured to expose the 302 so we can read the Location header.
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        };

        using var transientClient = new HttpClient(handler)
        {
            BaseAddress = client.BaseAddress
        };

        return await transientClient.SendAsync(request);
    }
}
