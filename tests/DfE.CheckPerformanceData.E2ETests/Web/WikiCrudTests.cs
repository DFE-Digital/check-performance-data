using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Web;

[Collection("E2E")]
[Trait("Category", "W4")]
public sealed class WikiCrudTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    private HttpRequestMessage NewRequest(HttpMethod method, string relativePath, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, $"{_fixture.BaseUrl}{relativePath}");
        if (content is not null) request.Content = content;
        return request;
    }

    // --- PostHelpCreate_RedirectsToSlug ---

    [Fact]
    public async Task PostHelpCreate_RedirectsToSlug()
    {
        var unique = $"e2e-{Guid.NewGuid():N}";
        var title = $"{unique}-create-target";

        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(_fixture.SeedClient, "/help?edit");

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Title", title),
            new KeyValuePair<string, string>("Content", "Body content for create-target."),
        });

        using var request = NewRequest(HttpMethod.Post, "/help/create", form);
        request.Headers.Add("X-XSRF-TOKEN", token);
        request.Headers.Add("Cookie", cookie);

        var response = await TestHttpClients.NoRedirect.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var location = response.Headers.Location!.ToString();
        Assert.Contains($"/help/{title.ToLowerInvariant()}", location);

        // Resolve the seeded page Id from the rendered tree and soft-delete so this spec
        // does not leak a wiki page on every CI run. The other tests in this file seed
        // via SeedHelpers (which tracks ids automatically); this one POSTs directly to
        // exercise the redirect contract, so the cleanup is hand-rolled here.
        try
        {
            var slug = location.TrimStart('/');
            const string prefix = "help/";
            if (slug.StartsWith(prefix, StringComparison.Ordinal))
            {
                slug = slug[prefix.Length..];
            }
            var queryIndex = slug.IndexOf('?');
            if (queryIndex >= 0)
            {
                slug = slug[..queryIndex];
            }

            var id = await SeedHelpers.ResolveIdFromTreeAsync(_fixture.SeedClient, slug);
            await SeedHelpers.SoftDeleteWikiPageAsync(_fixture.SeedClient, id);
        }
        catch
        {
            // Best-effort cleanup; cleanup failure must not mask the assertion outcome.
        }
    }

    // --- PostHelpDelete_Redirects302 ---

    [Fact]
    public async Task PostHelpDelete_Redirects302()
    {
        var unique = $"e2e-{Guid.NewGuid():N}";
        var title = $"{unique}-delete-target";
        var tracking = new List<int>();
        var id = await SeedHelpers.SeedWikiPageAsync(
            _fixture.SeedClient, title, "To be deleted.", parentId: null, tracking);

        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(_fixture.SeedClient, "/help?edit");

        using var request = NewRequest(
            HttpMethod.Post,
            $"/help/delete/{id}",
            new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()));
        request.Headers.Add("X-XSRF-TOKEN", token);
        request.Headers.Add("Cookie", cookie);

        var response = await TestHttpClients.NoRedirect.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    // --- GetHelpSlug_RendersTitle ---

    [Fact]
    public async Task GetHelpSlug_RendersTitle()
    {
        var unique = $"e2e-{Guid.NewGuid():N}";
        var title = $"{unique}-roundtrip-target";
        var tracking = new List<int>();
        var (id, slug) = await SeedHelpers.SeedWikiPageReturningSlugAsync(
            _fixture.SeedClient, title, "Round-trip body content.", parentId: null, tracking);

        try
        {
            var response = await _fixture.SeedClient.GetAsync($"/help/{slug}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            // SeedHelpers prepends "e2e-{guidA} " to the supplied title, then GenerateSlug
            // collapses the space to a dash. The unique block we control still appears verbatim
            // in the rendered title, so asserting against it is the safe substring check.
            Assert.Contains(unique, body);
        }
        finally
        {
            try { await SeedHelpers.SoftDeleteWikiPageAsync(_fixture.SeedClient, id); }
            catch { /* best-effort cleanup */ }
        }
    }
}
