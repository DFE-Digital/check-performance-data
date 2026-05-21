using System.Net;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Web;

[Collection("E2E")]
[Trait("Category", "W4")]
public sealed class HardDeleteTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    // The /help/deleted page renders one row per soft-deleted page; this regex finds the
    // page id wired into a delete-permanently trigger so the test can confirm a previously
    // seeded id is (or is no longer) present.
    private static readonly Regex DeletedRowIdPattern =
        new(
            "data-confirm-trigger=\"confirm-hard-delete-(?<id>\\d+)\"",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private async Task<bool> PageRowVisibleOnDeletedListAsync(int id)
    {
        // /help/deleted is editor-gated; SeedClient has no auto-cookie handling, so
        // attach the impersonation cookie explicitly. Without this the GET 302s
        // back to DSI sign-in (which then 400s on the test client).
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/help/deleted");
        var impersonation = TestHttpClients.ImpersonationCookieHeader;
        if (!string.IsNullOrEmpty(impersonation))
        {
            request.Headers.Add("Cookie", impersonation);
        }
        var response = await TestHttpClients.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        return DeletedRowIdPattern.Matches(body).Any(m => m.Groups["id"].Value == id.ToString());
    }

    // --- DeletePermanently_AsEditor_Removes_SoftDeleted_Page ---

    [Fact]
    public async Task DeletePermanently_AsEditor_Removes_SoftDeleted_Page()
    {
        var tracked = new List<int>();
        var id = await SeedHelpers.SeedWikiPageAsync(
            _fixture.SeedClient,
            title: "hard-delete-target",
            body: "Body of soft-deleted page.",
            parentId: null,
            tracked);
        await SeedHelpers.SoftDeleteWikiPageAsync(_fixture.SeedClient, id);

        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(_fixture.SeedClient, "/help/deleted");

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("editMode", "true"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        });

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_fixture.BaseUrl}/help/delete-permanently/{id}")
        {
            Content = form
        };
        request.Headers.Add("Cookie", cookie);

        var response = await TestHttpClients.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/help/deleted", response.Headers.Location?.ToString() ?? string.Empty);

        // The seeded page must no longer appear on /help/deleted.
        Assert.False(await PageRowVisibleOnDeletedListAsync(id));
    }

    // --- DeletePermanently_AsNonEditor_Redirects_To_AccessDenied ---

    [Fact]
    public async Task DeletePermanently_AsNonEditor_Redirects_To_AccessDenied()
    {
        var tracked = new List<int>();
        var id = await SeedHelpers.SeedWikiPageAsync(
            _fixture.SeedClient,
            title: "hard-delete-non-editor",
            body: "Body.",
            parentId: null,
            tracked);
        await SeedHelpers.SoftDeleteWikiPageAsync(_fixture.SeedClient, id);

        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_fixture.BaseUrl}/help/delete-permanently/{id}");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? string.Empty);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- DeletePermanently_MissingAntiforgery_Returns_400 ---

    [Fact]
    public async Task DeletePermanently_MissingAntiforgery_Returns_400()
    {
        var tracked = new List<int>();
        var id = await SeedHelpers.SeedWikiPageAsync(
            _fixture.SeedClient,
            title: "hard-delete-no-token",
            body: "Body.",
            parentId: null,
            tracked);
        await SeedHelpers.SoftDeleteWikiPageAsync(_fixture.SeedClient, id);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_fixture.BaseUrl}/help/delete-permanently/{id}");

        var response = await TestHttpClients.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- DeletePermanently_PageNotSoftDeleted_Rejects ---

    [Fact]
    public async Task DeletePermanently_PageNotSoftDeleted_Rejects()
    {
        var tracked = new List<int>();
        var id = await SeedHelpers.SeedWikiPageAsync(
            _fixture.SeedClient,
            title: "hard-delete-live-page",
            body: "Body.",
            parentId: null,
            tracked);

        try
        {
            var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(_fixture.SeedClient, "/help/deleted");

            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("editMode", "true"),
                new KeyValuePair<string, string>("__RequestVerificationToken", token)
            });

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_fixture.BaseUrl}/help/delete-permanently/{id}")
            {
                Content = form
            };
            request.Headers.Add("Cookie", cookie);

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/help/deleted", response.Headers.Location?.ToString() ?? string.Empty);

            // Capture the TempData session cookie set by the POST response so the
            // follow-up GET can read TempData["HardDeleteError"] (TempData is bound
            // to the session cookie, and TestHttpClients.SendAsync does not auto-
            // propagate cookies between requests).
            var tempDataCookies = new List<string>();
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var sc in setCookies)
                {
                    var nameValue = sc.Split(';', 2)[0].Trim();
                    if (!string.IsNullOrEmpty(nameValue))
                    {
                        tempDataCookies.Add(nameValue);
                    }
                }
            }

            // Follow the redirect and confirm the error banner surfaced via TempData.
            // /help/deleted is editor-gated — attach the impersonation cookie alongside
            // the antiforgery cookie + the TempData session cookies so the follow GET
            // authenticates AND can read the TempData payload.
            using var followRequest = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/help/deleted");
            var impersonation = TestHttpClients.ImpersonationCookieHeader;
            var cookieParts = new List<string>();
            if (!string.IsNullOrEmpty(impersonation)) cookieParts.Add(impersonation);
            cookieParts.Add(cookie);
            cookieParts.AddRange(tempDataCookies);
            followRequest.Headers.Add("Cookie", string.Join("; ", cookieParts));
            var followResponse = await TestHttpClients.SendAsync(followRequest);
            var followBody = await followResponse.Content.ReadAsStringAsync();
            Assert.Contains("govuk-error-summary", followBody);
            Assert.Contains("Could not delete the page", followBody);

            // The LIVE page row must still be present in the tree. /help is editor-
            // gated for the data-page-id attribute (which only renders in edit mode);
            // attach the impersonation cookie and hit /help?edit explicitly.
            using var treeRequest = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/help?edit");
            if (!string.IsNullOrEmpty(impersonation))
            {
                treeRequest.Headers.Add("Cookie", impersonation);
            }
            var treeResponse = await TestHttpClients.SendAsync(treeRequest);
            treeResponse.EnsureSuccessStatusCode();
            var treeBody = await treeResponse.Content.ReadAsStringAsync();
            Assert.Contains($"data-page-id=\"{id}\"", treeBody);
        }
        finally
        {
            // Best-effort cleanup of the live page seeded above.
            try
            {
                await SeedHelpers.SoftDeleteWikiPageAsync(_fixture.SeedClient, id);
            }
            catch
            {
                // best-effort
            }
        }
    }
}
