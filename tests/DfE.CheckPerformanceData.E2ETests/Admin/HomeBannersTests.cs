// tests/DfE.CheckPerformanceData.E2ETests/Admin/HomeBannersTests.cs
using System.Net;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// Start-page banners (#566). Driven over HTTP as admin — the admin role is topped up with every
// section on each start-up, whereas an editor on a database older than this feature has no
// home-banners grant until somebody ticks it on Role settings.
[Trait("Category", "FullRegression")]
[Collection("Http")]
public sealed partial class HomeBannersTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    // The guidance front page (#588 target). The root node is seeded as an unpublished
    // placeholder, and the catch-all resolver 404s an unpublished node.
    private static readonly Guid GuidanceFrontPageId = new("00000000-cd94-4a01-8f01-000000000004");

    // Publishes the guidance front page once so /guidance renders. Idempotent: does nothing
    // when it is already live. Call while impersonating an admin (the CMS write routes are
    // admin-gated), as the callers below already are.
    private async Task EnsureGuidanceFrontPageLiveAsync()
    {
        using var probe = await _fixture.AnonymousClient.GetAsync($"{_fixture.BaseUrl}/guidance");
        if (probe.StatusCode == HttpStatusCode.OK)
        {
            return;
        }

        await CmsSeedHelpers.AddWidgetAsync(_fixture.SeedClient, GuidanceFrontPageId, "0.0", "heading");
        await CmsSeedHelpers.UpdateWidgetAsync(_fixture.SeedClient, GuidanceFrontPageId, "0.0", "heading",
            new Dictionary<string, string> { ["level"] = "1", ["text"] = "Guidance" });
        await CmsSeedHelpers.PublishDraftAsync(_fixture.SeedClient, GuidanceFrontPageId);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task Create_ShowsOnTheStartPage_TurnOff_Hides_Delete_Removes()
    {
        var marker = $"e2e-banner-{Guid.NewGuid():N}";
        int? id = null;
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);
            var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(_fixture.SeedClient, "/dev/antiforgery-token");

            // Create: on, no dates → live now.
            var create = await PostFormAsync("/admin/home-banners/new", token, cookie,
                ("Heading", marker), ("Body", $"<p>{marker} body</p>"), ("IsEnabled", "true"));
            Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

            var list = await GetAsync(_fixture.SeedClient, "/admin/home-banners");
            Assert.Contains(marker, list);
            id = IdOfRowContaining(list, marker);

            // Start page (anonymous) shows it as a notification banner.
            var home = await GetAsync(_fixture.AnonymousClient, "/");
            Assert.Contains(marker, home);
            Assert.Contains($"home-banner-{id}-heading", home);

            // Turn it off → gone from the start page, still in the list.
            var off = await PostFormAsync($"/admin/home-banners/{id}/edit", token, cookie,
                ("Heading", marker), ("Body", $"<p>{marker} body</p>"), ("IsEnabled", "false"));
            Assert.Equal(HttpStatusCode.Redirect, off.StatusCode);
            Assert.DoesNotContain(marker, await GetAsync(_fixture.AnonymousClient, "/"));
            Assert.Contains(marker, await GetAsync(_fixture.SeedClient, "/admin/home-banners"));

            // Delete → gone from the list.
            var delete = await PostFormAsync($"/admin/home-banners/{id}/delete", token, cookie);
            Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
            id = null;
            Assert.DoesNotContain(marker, await GetAsync(_fixture.SeedClient, "/admin/home-banners"));
        }
        finally
        {
            try
            {
                // Best effort: never masks the real failure.
                await TryDeleteByMarkerAsync(marker, id);
            }
            finally
            {
                await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
            }
        }
    }

    // Saves dates through the real form binder: the GOV.UK date input posts Day/Month/Year as
    // separate fields and the time as two numbers. No JavaScript is involved, so this is also
    // the no-JS path.
    [Fact]
    public async Task DatedBanner_SavedThroughTheForm_IsLive_ThenScheduled()
    {
        var marker = $"e2e-dated-banner-{Guid.NewGuid():N}";
        int? id = null;
        var yesterday = DateTime.UtcNow.AddDays(-1);
        var tomorrow = DateTime.UtcNow.AddDays(1);
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);
            var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(_fixture.SeedClient, "/dev/antiforgery-token");

            // Yesterday 09:00 to tomorrow 17:00 â†’ live now.
            var create = await PostFormAsync("/admin/home-banners/new", token, cookie,
                ("Heading", marker), ("Body", $"<p>{marker} body</p>"), ("IsEnabled", "true"),
                ("ShowFromDate.Day", yesterday.Day.ToString()), ("ShowFromDate.Month", yesterday.Month.ToString()), ("ShowFromDate.Year", yesterday.Year.ToString()),
                ("ShowFromHour", "9"), ("ShowFromMinute", "0"),
                ("ShowUntilDate.Day", tomorrow.Day.ToString()), ("ShowUntilDate.Month", tomorrow.Month.ToString()), ("ShowUntilDate.Year", tomorrow.Year.ToString()),
                ("ShowUntilHour", "17"), ("ShowUntilMinute", "0"));
            Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

            var list = await GetAsync(_fixture.SeedClient, "/admin/home-banners");
            id = IdOfRowContaining(list, marker);
            Assert.Contains(">Live<", RowContaining(list, marker));
            Assert.Contains(marker, await GetAsync(_fixture.AnonymousClient, "/"));

            // Show from tomorrow 09:00, no Show until (blank fields, as the browser posts them) â†’ scheduled.
            var edit = await PostFormAsync($"/admin/home-banners/{id}/edit", token, cookie,
                ("Heading", marker), ("Body", $"<p>{marker} body</p>"), ("IsEnabled", "true"),
                ("ShowFromDate.Day", tomorrow.Day.ToString()), ("ShowFromDate.Month", tomorrow.Month.ToString()), ("ShowFromDate.Year", tomorrow.Year.ToString()),
                ("ShowFromHour", "9"), ("ShowFromMinute", "0"),
                ("ShowUntilDate.Day", ""), ("ShowUntilDate.Month", ""), ("ShowUntilDate.Year", ""),
                ("ShowUntilHour", ""), ("ShowUntilMinute", ""));
            Assert.Equal(HttpStatusCode.Redirect, edit.StatusCode);

            Assert.DoesNotContain(marker, await GetAsync(_fixture.AnonymousClient, "/"));
            Assert.Contains(">Scheduled<", RowContaining(await GetAsync(_fixture.SeedClient, "/admin/home-banners"), marker));
        }
        finally
        {
            try
            {
                await TryDeleteByMarkerAsync(marker, id);
            }
            finally
            {
                await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
            }
        }
    }

    // The list row that holds the marker, from the marker to the end of its <tr>.
    private static string RowContaining(string listHtml, string marker)
    {
        var start = listHtml.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{marker}' is not in the banner list");
        var end = listHtml.IndexOf("</tr>", start, StringComparison.Ordinal);
        return end < 0 ? listHtml[start..] : listHtml[start..end];
    }

    // Removes a banner the test may have created but not finished with. When the id was never
    // captured, the marker in the admin list finds it. Swallows failures so the original
    // exception survives.
    private async Task TryDeleteByMarkerAsync(string marker, int? id)
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);
            if (id is null)
            {
                var list = await GetAsync(_fixture.SeedClient, "/admin/home-banners");
                if (!list.Contains(marker, StringComparison.Ordinal))
                {
                    return;
                }
                id = IdOfRowContaining(list, marker);
            }
            var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(_fixture.SeedClient, "/dev/antiforgery-token");
            await PostFormAsync($"/admin/home-banners/{id}/delete", token, cookie);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Banner cleanup for '{marker}' failed: {ex}");
        }
    }

    [Fact]
    public async Task HomeBanners_AsUnprivileged_Returns404()
    {
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/admin/home-banners");
            var response = await _fixture.SeedClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    [Fact]
    public async Task StartPage_NeverRendersAnEmptyBannerRegion()
    {
        // The component renders nothing when no banner is live; this pins that there is no
        // wrapper with a blank id left behind.
        var home = await GetAsync(_fixture.AnonymousClient, "/");
        Assert.DoesNotContain("home-banner--title", home);
    }

    [Fact]
    public async Task GuidanceFrontPage_NeverRendersAnEmptyBannerRegion()
    {
        // Same guarantee as the start page (#566): with no live banner the guidance front page
        // renders no wrapper, not even a blank one.
        await AuthHelpers.ImpersonateAsAdminAsync(_fixture);
        try
        {
            await EnsureGuidanceFrontPageLiveAsync();
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }

        var guidance = await GetAsync(_fixture.AnonymousClient, "/guidance");
        Assert.DoesNotContain("home-banner--title", guidance);
    }

    // #588: a live banner appears on the guidance front page too, with no extra editorial step —
    // publishing it once is enough for both the start page and /guidance.
    [Fact]
    [Trait("Category", "Smoke")]
    public async Task BannerLive_ShowsOnBothHomeAndGuidance_AndOffRemovesFromBoth()
    {
        var marker = $"e2e-guidance-banner-{Guid.NewGuid():N}";
        int? id = null;
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);
            var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(_fixture.SeedClient, "/dev/antiforgery-token");

            var create = await PostFormAsync("/admin/home-banners/new", token, cookie,
                ("Heading", marker), ("Body", $"<p>{marker} body</p>"), ("IsEnabled", "true"));
            Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
            id = IdOfRowContaining(await GetAsync(_fixture.SeedClient, "/admin/home-banners"), marker);

            // Make sure /guidance is published before asserting the new behaviour.
            await EnsureGuidanceFrontPageLiveAsync();

            // Present on the start page (the #566 behaviour, unchanged) ...
            Assert.Contains(marker, await GetAsync(_fixture.AnonymousClient, "/"));

            // ... and now on the guidance front page (#588).
            var guidance = await GetAsync(_fixture.AnonymousClient, "/guidance");
            Assert.Contains(marker, guidance);
            Assert.Contains($"home-banner-{id}-heading", guidance);

            // Turning it off removes it from both pages, with no extra guidance-specific step.
            var off = await PostFormAsync($"/admin/home-banners/{id}/edit", token, cookie,
                ("Heading", marker), ("Body", $"<p>{marker} body</p>"), ("IsEnabled", "false"));
            Assert.Equal(HttpStatusCode.Redirect, off.StatusCode);
            Assert.DoesNotContain(marker, await GetAsync(_fixture.AnonymousClient, "/"));
            Assert.DoesNotContain(marker, await GetAsync(_fixture.AnonymousClient, "/guidance"));
        }
        finally
        {
            try
            {
                await TryDeleteByMarkerAsync(marker, id);
            }
            finally
            {
                await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
            }
        }
    }

    // #588: the banners are scoped to the guidance front page only — an ordinary content page
    // with the same live banner present must not render it.
    [Fact]
    public async Task BannerLive_DoesNotShowOnGuidanceSubPageOrOtherContentPage()
    {
        var marker = $"e2e-scope-banner-{Guid.NewGuid():N}";
        var segment = $"e2e-scope-{Guid.NewGuid():N}";
        int? id = null;
        Guid? pageId = null;
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);
            var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(_fixture.SeedClient, "/dev/antiforgery-token");

            var create = await PostFormAsync("/admin/home-banners/new", token, cookie,
                ("Heading", marker), ("Body", $"<p>{marker} body</p>"), ("IsEnabled", "true"));
            Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
            id = IdOfRowContaining(await GetAsync(_fixture.SeedClient, "/admin/home-banners"), marker);

            // Make sure /guidance is published before asserting the new behaviour.
            await EnsureGuidanceFrontPageLiveAsync();

            // A separate, published content page elsewhere in the tree.
            pageId = await CmsSeedHelpers.CreatePageNodeAsync(
                _fixture.SeedClient, FixtureContent.RootId, "content", segment, "E2E banner scope page");
            await CmsSeedHelpers.AddWidgetAsync(_fixture.SeedClient, pageId.Value, "0.0", "heading");
            await CmsSeedHelpers.UpdateWidgetAsync(_fixture.SeedClient, pageId.Value, "0.0", "heading",
                new Dictionary<string, string> { ["level"] = "1", ["text"] = "Scoped page" });
            await CmsSeedHelpers.PublishDraftAsync(_fixture.SeedClient, pageId.Value);

            // The banner is live and shows on guidance ...
            Assert.Contains(marker, await GetAsync(_fixture.AnonymousClient, "/guidance"));

            // ... but the ordinary content page is clean.
            var other = await GetAsync(_fixture.AnonymousClient, $"{FixtureContent.RootPath}/{segment}");
            Assert.DoesNotContain(marker, other);
            Assert.DoesNotContain($"home-banner-{id}-heading", other);

            // A guidance sub-page is excluded too (its path contains a "/"), as is the shared
            // content template's other well-known page (help/not-found). Neither need be a 200.
            Assert.DoesNotContain(marker,
                await GetAllowingStatusAsync(_fixture.AnonymousClient, "/guidance/ks4-test"));
            Assert.DoesNotContain(marker,
                await GetAllowingStatusAsync(_fixture.AnonymousClient, "/help/not-found"));
        }
        finally
        {
            try
            {
                if (pageId is not null)
                {
                    await CmsSeedHelpers.TryDeletePageAsync(_fixture.SeedClient, pageId.Value);
                }
                await TryDeleteByMarkerAsync(marker, id);
            }
            finally
            {
                await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
            }
        }
    }

    // The list renders one <tr> per banner; the first edit link after the marker belongs to its row.
    private static int IdOfRowContaining(string listHtml, string marker)
    {
        var start = listHtml.IndexOf(marker, StringComparison.Ordinal);
        var match = EditLink().Match(listHtml, start);
        Assert.True(match.Success, "No edit link after the created banner's heading");
        return int.Parse(match.Groups[1].Value);
    }

    private async Task<string> GetAsync(TestHttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}{path}");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // Like GetAsync but for paths that may legitimately answer 404 (e.g. the help/not-found
    // content page), so a "banner must not appear here" assertion is not blocked by status.
    private async Task<string> GetAllowingStatusAsync(TestHttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}{path}");
        var response = await client.SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<HttpResponseMessage> PostFormAsync(string path, string token, string cookie, params (string Key, string Value)[] fields)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_fixture.BaseUrl}{path}")
        {
            Content = new FormUrlEncodedContent(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)))
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        request.Headers.Add("Cookie", cookie);
        return await _fixture.SeedClient.SendAsync(request);
    }

    [GeneratedRegex("/admin/home-banners/(\\d+)/edit")]
    private static partial Regex EditLink();
}
