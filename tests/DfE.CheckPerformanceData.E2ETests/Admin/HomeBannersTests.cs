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
