using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// The media library admin page, its gate, and the CSRF guard on its upload endpoint. Driven as admin: the admin role is topped up with
// every section on each start-up, whereas an editor on a database older than the media section has
// no media grant until somebody ticks it on Role settings.
[Trait("Category", "FullRegression")]
[Collection("Http")]
public sealed class MediaLibraryTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    [Fact]
    public async Task Admin_SeesTheMediaLibrary_AndItsTileInTheCmsNav()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);

            using var page = await _fixture.SeedClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/admin/media"));
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            var html = await page.Content.ReadAsStringAsync();
            Assert.Contains("<h1 class=\"govuk-heading-xl\">Media library</h1>", html);
            Assert.True(html.Contains("data-media-library-empty") || html.Contains("data-media-library-list"));

            using var landing = await _fixture.SeedClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/admin"));
            var landingHtml = await landing.Content.ReadAsStringAsync();
            Assert.Contains("href=\"/admin/media\"", landingHtml);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    // A signed-in user with no media grant gets 404, the same as every other admin section.
    [Fact]
    public async Task UserWithoutTheMediaSection_GetsNotFound()
    {
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);

            using var response = await _fixture.SeedClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/admin/media"));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    private static MultipartFormDataContent UploadBody(string fileName, byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent { { file, "files", fileName } };
    }

    // The upload script sends the token only in the X-XSRF-TOKEN header. Without it, the
    // upload is refused (400, or 404 once the status-code page re-executes it) and nothing is stored.
    [Fact]
    public async Task Upload_WithoutTheAntiforgeryToken_IsRefused()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);
            var (_, cookie) = await AntiforgeryHelpers.ScrapeAsync(_fixture.SeedClient, "/admin/media");
            var name = $"e2e-{Guid.NewGuid():N}-no-token.png";

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_fixture.BaseUrl}/admin/media/upload")
            {
                Content = UploadBody(name, MediaFixtures.UniquePng()),
            };
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("Cookie", $"{_fixture.SeedClient.ImpersonationCookieHeader}; {cookie}");
            using var response = await _fixture.SeedClient.SendAsync(request);

            Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
                $"Expected the CSRF guard to refuse the upload; got {(int)response.StatusCode}.");
            using var list = await _fixture.SeedClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/admin/media?search={name}"));
            // The search box repeats the term, so look for the empty-result message instead.
            Assert.Contains("data-media-library-empty", await list.Content.ReadAsStringAsync());
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    [Fact]
    public async Task Upload_WithTheTokenInTheHeader_StoresTheImage_AndAnswersJson()
    {
        try
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);
            var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(_fixture.SeedClient, "/admin/media");
            var name = $"e2e-{Guid.NewGuid():N}-header.png";

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_fixture.BaseUrl}/admin/media/upload")
            {
                Content = UploadBody(name, MediaFixtures.UniquePng()),
            };
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("X-XSRF-TOKEN", token);
            request.Headers.Add("Cookie", $"{_fixture.SeedClient.ImpersonationCookieHeader}; {cookie}");
            using var response = await _fixture.SeedClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"outcomeName\":\"created\"", body);
            Assert.Contains(name, body);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }
}
