using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// The media library admin page and its gate. Driven as admin: the admin role is topped up with
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
}
