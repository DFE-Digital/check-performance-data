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

        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/help/antiforgery-token");

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

    // Edits an existing wiki page (POST /help/edit/{id}) which appends a new row to
    // WikiPageVersions. Returns the page slug after the edit (slug may change if Title
    // changes, but for revert-modal tests we keep the original prefixed title).
    public static async Task<string> EditWikiPageAsync(
        HttpClient client,
        int id,
        string title,
        string body)
    {
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/help/antiforgery-token");

        var formFields = new[]
        {
            new KeyValuePair<string, string>("Title", title),
            new KeyValuePair<string, string>("Content", body),
            new KeyValuePair<string, string>("editMode", "true"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/help/edit/{id}")
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
                $"Editing wiki page {id} returned {(int)response.StatusCode} {response.StatusCode}; expected 302.");
        }

        var location = response.Headers.Location?.ToString() ?? string.Empty;
        var slugPath = location;
        var queryIndex = slugPath.IndexOf('?');
        if (queryIndex >= 0) slugPath = slugPath[..queryIndex];
        const string prefix = "/help/";
        return slugPath.StartsWith(prefix, StringComparison.Ordinal)
            ? slugPath[prefix.Length..]
            : slugPath;
    }

    // Saves a new value over an existing content block key (POST /content-block/save),
    // which appends a row to ContentBlockVersions.
    public static async Task EditContentBlockAsync(
        HttpClient client,
        string key,
        string newValue)
    {
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/help/antiforgery-token");

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

        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/help/antiforgery-token");

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

    // Sweeps any wiki page still visible in the tree whose slug starts with "e2e-"
    // and soft-deletes it. The e2e- prefix is the seed "tag" (every test seed sets it
    // via SeedWikiPageReturningSlugAsync) so this catches orphans left behind when a
    // test crashed before SeedingPageTest.DisposeAsync ran its tracked cleanup. Best-
    // effort: per-page failures are swallowed so the sweep doesn't mask test outcomes.
    public static async Task SweepOrphanE2eWikiPagesAsync(HttpClient client)
    {
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync("/help");
        }
        catch
        {
            return; // app is unreachable; nothing we can do at teardown
        }

        if (!response.IsSuccessStatusCode) return;

        var html = await response.Content.ReadAsStringAsync();
        var ids = new HashSet<int>();

        foreach (Match match in SlugToIdPattern.Matches(html))
        {
            var slug = match.Groups["slug"].Value;
            if (!slug.StartsWith("e2e-", StringComparison.Ordinal)) continue;
            if (int.TryParse(match.Groups["id"].Value, out var id)) ids.Add(id);
        }

        foreach (var id in ids)
        {
            try
            {
                await SoftDeleteWikiPageAsync(client, id);
            }
            catch
            {
                // best-effort
            }
        }
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

    public static async Task SoftDeleteWikiPageAsync(HttpClient client, int id)
    {
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/help/antiforgery-token");

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

    private static Task<HttpResponseMessage> SendWithoutFollowingRedirects(
        HttpClient client,
        HttpRequestMessage request)
    {
        // TestHttpClients.NoRedirect has no BaseAddress; resolve relative request URIs
        // against the caller's client so seed POSTs continue to be written as
        // "/help/create" etc.
        if (request.RequestUri is { IsAbsoluteUri: false } && client.BaseAddress is not null)
        {
            request.RequestUri = new Uri(client.BaseAddress, request.RequestUri);
        }

        return TestHttpClients.SendAsync(request);
    }
}
