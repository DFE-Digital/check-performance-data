using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

// HTTP-level helpers that drive the CMS admin surface — create a PageNode, drop a
// widget on it, update widget props, publish, delete. Companion to SeedHelpers.cs
// (which handles content-blocks + queue seeds). Every helper scrapes its own
// antiforgery pair per call so a token issued for one endpoint doesn't leak into
// another.
public static class CmsSeedHelpers
{
    // Stable root Guid seeded by DefaultPageNodeRoots. Use as parentId when placing a
    // test page under /help. Segment must be unique per test to avoid the CMS
    // rejecting the second create with "A page already exists at that path".
    public static readonly Guid HelpRootId = new("00000000-cd94-4a01-8f01-000000000003");

    // Creates a PageNode under the given parent. Returns the new node's Guid parsed
    // from the redirect Location. Throws with the response body if the CMS returns
    // 200 instead of the expected 302 (which is how it surfaces validation errors).
    public static async Task<Guid> CreatePageNodeAsync(
        HttpClient client,
        Guid? parentId,
        string pageType,
        string segment,
        string title)
    {
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/dev/antiforgery-token");

        var fields = new List<KeyValuePair<string, string>>
        {
            new("pageType", pageType),
            new("segment", segment),
            new("title", title),
            new("__RequestVerificationToken", token),
        };
        if (parentId.HasValue)
        {
            fields.Add(new("parentId", parentId.Value.ToString()));
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, "/admin/pages/create")
        {
            Content = new FormUrlEncodedContent(fields),
        };
        req.Headers.Add("Cookie", cookie);
        req.RequestUri = new Uri(client.BaseAddress!, req.RequestUri!);

        var response = await TestHttpClients.SendAsync(req);
        if (response.StatusCode != HttpStatusCode.Found && response.StatusCode != HttpStatusCode.Redirect)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"POST /admin/pages/create returned {(int)response.StatusCode} instead of 302. Body head: "
                + body[..Math.Min(400, body.Length)]);
        }

        // Location: /admin/pages/{id}/edit — parse the Guid segment.
        var location = response.Headers.Location?.ToString()
            ?? throw new InvalidOperationException("POST /admin/pages/create did not return a Location header.");
        var segments = location.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            if (Guid.TryParse(segments[i], out var id)) return id;
        }
        throw new InvalidOperationException($"No Guid found in Location '{location}'.");
    }

    // Drops a widget of the given type at the given content-tree path (typically "0.0"
    // for the first widget on an empty content page). Uses the widget's default props.
    public static async Task AddWidgetAsync(HttpClient client, Guid pageId, string path, string widgetType)
    {
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/dev/antiforgery-token");

        using var req = new HttpRequestMessage(HttpMethod.Post, $"/admin/pages/{pageId}/content/add")
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("path", path),
                new KeyValuePair<string, string>("widgetType", widgetType),
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
            }),
        };
        req.Headers.Add("Cookie", cookie);
        req.RequestUri = new Uri(client.BaseAddress!, req.RequestUri!);

        var response = await TestHttpClients.SendAsync(req);
        if (response.StatusCode != HttpStatusCode.Found && response.StatusCode != HttpStatusCode.Redirect)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"POST /admin/pages/{pageId}/content/add returned {(int)response.StatusCode}. Body head: "
                + body[..Math.Min(400, body.Length)]);
        }
    }

    // Overwrites the props of an existing widget. Only prop keys present in the
    // widget's registry entry survive (WidgetPropsBuilder drops unknowns).
    public static async Task UpdateWidgetAsync(
        HttpClient client,
        Guid pageId,
        string path,
        string widgetType,
        IEnumerable<KeyValuePair<string, string>> props)
    {
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/dev/antiforgery-token");

        var fields = new List<KeyValuePair<string, string>>
        {
            new("path", path),
            new("type", widgetType),
            new("__RequestVerificationToken", token),
        };
        foreach (var kv in props)
        {
            fields.Add(new KeyValuePair<string, string>($"props[{kv.Key}]", kv.Value));
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, $"/admin/pages/{pageId}/content/widget")
        {
            Content = new FormUrlEncodedContent(fields),
        };
        req.Headers.Add("Cookie", cookie);
        req.RequestUri = new Uri(client.BaseAddress!, req.RequestUri!);

        var response = await TestHttpClients.SendAsync(req);
        if (response.StatusCode != HttpStatusCode.Found && response.StatusCode != HttpStatusCode.Redirect)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"POST /admin/pages/{pageId}/content/widget returned {(int)response.StatusCode}. Body head: "
                + body[..Math.Min(400, body.Length)]);
        }
    }

    // Publishes the working draft immediately (from=UtcNow, open-ended). Idempotent —
    // republishes the same draft if called twice.
    public static async Task PublishDraftAsync(HttpClient client, Guid pageId)
    {
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/dev/antiforgery-token");

        using var req = new HttpRequestMessage(HttpMethod.Post, $"/admin/pages/{pageId}/publish-draft")
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
            }),
        };
        req.Headers.Add("Cookie", cookie);
        req.RequestUri = new Uri(client.BaseAddress!, req.RequestUri!);

        var response = await TestHttpClients.SendAsync(req);
        if (response.StatusCode != HttpStatusCode.Found && response.StatusCode != HttpStatusCode.Redirect)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"POST /admin/pages/{pageId}/publish-draft returned {(int)response.StatusCode}. Body head: "
                + body[..Math.Min(400, body.Length)]);
        }
    }

    // Best-effort teardown for tests that want to leave the CMS clean. The route
    // rejects nodes with children; leaf test pages are fine. Failure is swallowed so
    // it can't mask the outer test outcome.
    public static async Task TryDeletePageAsync(HttpClient client, Guid pageId)
    {
        try
        {
            var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/dev/antiforgery-token");

            using var req = new HttpRequestMessage(HttpMethod.Post, $"/admin/pages/{pageId}/delete")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("__RequestVerificationToken", token),
                }),
            };
            req.Headers.Add("Cookie", cookie);
            req.RequestUri = new Uri(client.BaseAddress!, req.RequestUri!);
            using var _ = await TestHttpClients.SendAsync(req);
        }
        catch
        {
            // teardown is best-effort
        }
    }

    // Changes an admin setting via POST /admin/settings/save. Swaps impersonation to
    // admin for the write and restores editor in a finally so the fixture's default
    // role is preserved.
    public static async Task SetAdminSettingAsync(
        PlaywrightFixture fixture,
        string key,
        string value)
    {
        await AuthHelpers.ImpersonateAsAdminAsync(fixture);
        try
        {
            var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(fixture.SeedClient, "/dev/antiforgery-token");

            using var req = new HttpRequestMessage(HttpMethod.Post, "/admin/settings/save")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("key", key),
                    new KeyValuePair<string, string>("value", value),
                    new KeyValuePair<string, string>("__RequestVerificationToken", token),
                }),
            };
            req.Headers.Add("Cookie", cookie);
            req.RequestUri = new Uri(fixture.SeedClient.BaseAddress!, req.RequestUri!);

            var response = await TestHttpClients.SendAsync(req);
            if (response.StatusCode != HttpStatusCode.Found && response.StatusCode != HttpStatusCode.Redirect)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"POST /admin/settings/save returned {(int)response.StatusCode}. Body head: "
                    + body[..Math.Min(400, body.Length)]);
            }
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(fixture);
        }
    }
}
