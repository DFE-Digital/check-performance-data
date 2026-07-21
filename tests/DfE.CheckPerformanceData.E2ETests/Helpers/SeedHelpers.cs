using System.Net;
using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

public static class SeedHelpers
{
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

        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/dev/antiforgery-token");

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

        var id = await ResolveWikiPageIdBySlugAsync(client, seededSlug);
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
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/dev/antiforgery-token");

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

    // Server-side teardown: POSTs to /dev/wiki-e2e-cleanup which walks the wiki tree,
    // finds every page whose slug starts "e2e-", and soft-deletes it. Called from
    // PlaywrightFixture.DisposeAsync so orphans from a crashed test don't accumulate.
    // Best-effort: swallowed exceptions preserve the outer test outcome.
    public static async Task SweepOrphanE2eWikiPagesAsync(HttpClient client)
    {
        try
        {
            var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/dev/antiforgery-token");
            var baseAddress = client.BaseAddress
                ?? throw new InvalidOperationException("SeedClient must have a BaseAddress.");

            using var request = new HttpRequestMessage(
                HttpMethod.Post, new Uri(baseAddress, "/dev/wiki-e2e-cleanup"))
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("__RequestVerificationToken", token)
                })
            };
            request.Headers.Add("Cookie", cookie);
            request.Headers.Add("X-XSRF-TOKEN", token);
            using var _ = await TestHttpClients.SendAsync(request);
        }
        catch
        {
            // best-effort; teardown must not mask test outcomes
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
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/dev/antiforgery-token");

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

    // Resolves a wiki page slug to its numeric id via the dev-only lookup endpoint. Used
    // by SeedWikiPageReturningSlugAsync (which knows the slug the create redirect returned
    // and needs the id for the tracking list) and by any test whose flow needs the id
    // after a slug-based creation. Uses TestHttpClients.SendAsync so the impersonation
    // cookie attaches — the endpoint is editor-gated and the SeedClient does not manage
    // cookies of its own.
    internal static async Task<int> ResolveWikiPageIdBySlugAsync(HttpClient client, string slugPath)
    {
        var baseAddress = client.BaseAddress
            ?? throw new InvalidOperationException("SeedClient must have a BaseAddress.");
        var uri = new Uri(baseAddress, $"/dev/wiki-slug-to-id?slug={Uri.EscapeDataString(slugPath)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var response = await TestHttpClients.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            var body = (await response.Content.ReadAsStringAsync()).Trim();
            if (int.TryParse(body, out var id))
            {
                return id;
            }
        }

        throw new InvalidOperationException(
            $"Could not resolve wiki page id for slug '{slugPath}' via /dev/wiki-slug-to-id (status {(int)response.StatusCode}).");
    }

    // Seeds a conflicting ChangeRequest via the dev pipeline endpoint so E2E tests can
    // exercise duplicate detection. Returns the seeded reference number.
    // - userId: the submitter's user ID (use the impersonated user's id for self-duplicates,
    //   a different GUID for other-submitted duplicates)
    // - pupilUpn: the UPN of the pupil to conflict on
    // - submittedByName: display name shown in "Your colleague {name}..." messages
    // - windowId: the checking window to seed into (defaults to the seeded KS4 June window
    //   where SeedPupilData uploads pupil blob data)
    /// <param name="userId">The submitter's user ID (use the impersonated user's id for self-duplicates,
    ///   a different GUID for other-submitted duplicates).</param>
    /// <param name="pupilUpn">The UPN of the pupil to conflict on.</param>
    /// <param name="submittedByName">Display name shown in "Your colleague {name}..." messages.</param>
    /// <param name="windowId">The checking window to seed into (defaults to the seeded KS4 June window).</param>
    /// <param name="organisationUrn">The organisation URN — must match the impersonated user's URN
    ///   (142313 for Kingsmead School) so that CheckForConflictAsync finds the conflict.</param>
    public static async Task<string> SeedConflictRequestAsync(
        HttpClient client,
        Guid userId,
        string pupilUpn,
        string submittedByName,
        Guid windowId = default,
        long organisationUrn = 142313)
    {
        if (windowId == Guid.Empty)
            windowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

        var baseAddress = client.BaseAddress
            ?? throw new InvalidOperationException("SeedClient must have a BaseAddress.");

        // Use the seeded KS4 June checking window where SeedPupilData uploads pupil blob data.
        // The DevPipelineRunner resolves the pupil from blob storage via UPN + windowId + laestab.
        // The organisationUrn must match the impersonated user's org URN (142313 for Kingsmead School)
        // so that CheckForConflictAsync (which filters by OrganisationUrn) will find the conflict.
        var uri = new Uri(baseAddress,
            $"/dev/queues/submit-request" +
            $"?userId={userId}" +
            $"&windowId={windowId}" +
            $"&urn={organisationUrn}" +
            $"&pupilUpn={Uri.EscapeDataString(pupilUpn)}" +
            $"&userEmail={Uri.EscapeDataString(submittedByName.ToLower().Replace(" ", ".") + "@example.com")}" +
            $"&laestab={Uri.EscapeDataString("860/4070")}");

        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        var response = await SendWithoutFollowingRedirects(client, request);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        // Response JSON contains "referenceNumber": "DEV-..."
        var match = ReferenceNumberPattern.Match(body);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Could not parse seeded conflict reference from response: {body}");
        }

        return match.Groups["ref"].Value;
    }

    private static readonly Regex ReferenceNumberPattern =
        new("\"referenceNumber\"\\s*:\\s*\"(?<ref>[^\"]+)\"", RegexOptions.Compiled);

    // Deletes all ChangeRequests whose reference starts with "DEV-" so duplicate-detection
    // E2E tests don't leave stale conflicts that poison subsequent tests.
    public static async Task CleanupDevRequestsAsync(HttpClient client)
    {
        var baseAddress = client.BaseAddress
            ?? throw new InvalidOperationException("SeedClient must have a BaseAddress.");

        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(baseAddress, "/dev/queues/cleanup-e2e-requests"));
        using var response = await TestHttpClients.SendAsync(request);
        // Best-effort: ignore failures (endpoint might not exist on older deploys)
        if (!response.IsSuccessStatusCode) { }
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
