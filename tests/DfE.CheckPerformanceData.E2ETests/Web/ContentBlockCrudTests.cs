using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Web;

[Collection("E2E")]
[Trait("Category", "W4")]
public sealed class ContentBlockCrudTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    // --- PostSave_Redirects302 ---

    [Fact]
    public async Task PostSave_Redirects302()
    {
        // Content-block leak accepted: no DELETE route on ContentBlockController. UUID-prefixed
        // keys make leaked test data harmless across runs.
        var unique = $"e2e-{Guid.NewGuid():N}";
        var key = $"{unique}-block";

        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(_fixture.SeedClient, "/help?edit");

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Key", key),
            new KeyValuePair<string, string>("BlockType", "Content"),
            new KeyValuePair<string, string>("Value", $"<p>e2e marker {unique}</p>"),
            new KeyValuePair<string, string>("OriginalValue", string.Empty),
            new KeyValuePair<string, string>("ReturnUrl", "/"),
        });

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_fixture.BaseUrl}/content-block/save")
        {
            Content = form
        };
        request.Headers.Add("X-XSRF-TOKEN", token);
        request.Headers.Add("Cookie", cookie);

        var response = await TestHttpClients.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    // --- PostSave_AsNonEditor_Returns403 ---

    [Fact]
    public async Task PostSave_AsNonEditor_Returns403()
    {
        var unique = $"e2e-{Guid.NewGuid():N}";
        var key = $"{unique}-block";

        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(_fixture.SeedClient, "/help?edit");

        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);

            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("Key", key),
                new KeyValuePair<string, string>("BlockType", "Content"),
                new KeyValuePair<string, string>("Value", $"<p>e2e marker {unique}</p>"),
                new KeyValuePair<string, string>("OriginalValue", string.Empty),
                new KeyValuePair<string, string>("ReturnUrl", "/"),
            });

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_fixture.BaseUrl}/content-block/save")
            {
                Content = form
            };
            request.Headers.Add("X-XSRF-TOKEN", token);
            request.Headers.Add("Cookie", cookie);

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? string.Empty);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- PostRevert_AsNonEditor_Returns403 ---

    [Fact]
    public async Task PostRevert_AsNonEditor_Returns403()
    {
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(_fixture.SeedClient, "/help?edit");

        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);

            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("returnUrl", "/"),
            });

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_fixture.BaseUrl}/content-block/revert/some-key/1")
            {
                Content = form
            };
            request.Headers.Add("X-XSRF-TOKEN", token);
            request.Headers.Add("Cookie", cookie);

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? string.Empty);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // --- Versions_AsNonEditor_Redirects_To_AccessDenied ---

    [Fact]
    public async Task Versions_AsNonEditor_Redirects_To_AccessDenied()
    {
        // The action returns 404 for missing keys, but authorization runs first and
        // short-circuits with a 302 to AccessDenied before the controller body executes,
        // so we assert on the redirect (not the 404). GET — no antiforgery scrape needed.
        var key = $"e2e-{Guid.NewGuid():N}-block";

        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_fixture.BaseUrl}/content-block/versions/{key}");

            var response = await TestHttpClients.SendAsync(request);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? string.Empty);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }
}
