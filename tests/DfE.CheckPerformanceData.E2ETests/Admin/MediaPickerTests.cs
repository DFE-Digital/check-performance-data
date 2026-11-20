using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// The media picker endpoints over HTTP. They are gated on content-pages, not media: the editor
// (who edits pages) may browse the library and upload into it, a user with no admin section gets
// 404, and an upload without the antiforgery token in its header is refused and stores nothing.
[Trait("Category", "FullRegression")]
[Collection("Http")]
public sealed class MediaPickerTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    private Task<HttpResponseMessage> GetAsync(string path) =>
        _fixture.SeedClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}{path}"));

    private static MultipartFormDataContent UploadBody(string fileName, byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    [Fact]
    public async Task AnEditor_CanBrowseThePicker_AsJson()
    {
        await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        await MediaSeedHelpers.SeedSampleAsync(_fixture.SeedClient, "png");

        using var response = await GetAsync("/admin/media/picker?q=sample&page=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("application/json", response.Content.Headers.ContentType?.ToString());
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"items\":[", body);
        Assert.Contains("\"thumbUrl\":\"/media/", body);
        Assert.Contains("\"totalPages\":", body);
    }

    [Fact]
    public async Task AnEditor_GetsALivePreview_FromTheWidgetsOwnView()
    {
        await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        var sample = await MediaSeedHelpers.SeedSampleAsync(_fixture.SeedClient, "png");

        using var response = await GetAsync(
            $"/admin/media/picker/preview?mediaId={sample.Id}&alt=Blocks&width=one-half&crop=0.25,0,0.5,1&aspect=1:1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<figure class=\"app-image app-image--one-half\"", html);
        Assert.Contains("crop=0.25,0,0.5,1", html);
        Assert.Contains("sig=", html);
    }

    [Theory]
    [InlineData("/admin/media/picker")]
    [InlineData("/admin/media/picker/preview")]
    public async Task AUserWithNoAdminSection_GetsNotFound(string path)
    {
        try
        {
            await AuthHelpers.ImpersonateAsUnprivilegedUserAsync(_fixture);

            using var response = await GetAsync(path);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }

    [Fact]
    public async Task AnUpload_WithoutTheAntiforgeryToken_IsRefused_AndStoresNothing()
    {
        await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        var (_, cookie) = await AntiforgeryHelpers.ScrapeAsync(_fixture.SeedClient, "/dev/antiforgery-token");
        var name = $"e2e-{Guid.NewGuid():N}-picker-no-token.png";

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_fixture.BaseUrl}/admin/media/picker/upload")
        {
            Content = UploadBody(name, MediaFixtures.UniquePng()),
        };
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("Cookie", $"{_fixture.SeedClient.ImpersonationCookieHeader}; {cookie}");
        using var response = await _fixture.SeedClient.SendAsync(request);

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
            $"Expected the CSRF guard to refuse the upload; got {(int)response.StatusCode}.");
        using var list = await GetAsync($"/admin/media/picker?q={name}");
        Assert.Contains("\"totalCount\":0", await list.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnEditor_CanUpload_WithTheTokenInTheHeader()
    {
        var name = $"e2e-{Guid.NewGuid():N}-picker.png";
        try
        {
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
            var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(_fixture.SeedClient, "/dev/antiforgery-token");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_fixture.BaseUrl}/admin/media/picker/upload")
            {
                Content = UploadBody(name, MediaFixtures.UniquePng()),
            };
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("X-XSRF-TOKEN", token);
            request.Headers.Add("Cookie", $"{_fixture.SeedClient.ImpersonationCookieHeader}; {cookie}");
            using var response = await _fixture.SeedClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains($"\"fileName\":\"{name}\"", body);
            Assert.Contains("\"duplicate\":false", body);
            Assert.Contains("\"canCrop\":true", body);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);
            await MediaSeedHelpers.DeleteMatchingAsync(_fixture.SeedClient, name);
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }
}
