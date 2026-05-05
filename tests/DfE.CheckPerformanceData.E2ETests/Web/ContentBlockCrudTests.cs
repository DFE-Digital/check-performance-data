using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;

namespace DfE.CheckPerformanceData.E2ETests.Web;

[Collection("E2E")]
[Trait("Category", "W4")]
public sealed class ContentBlockCrudTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    private HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        return new HttpClient(handler) { BaseAddress = new Uri(_fixture.BaseUrl) };
    }

    // --- PostSave_Redirects302 ---

    [Fact]
    public async Task PostSave_Redirects302()
    {
        var unique = $"e2e-{Guid.NewGuid():N}";
        var key = $"{unique}-block";

        using var client = CreateClient();
        var (token, cookie) = await _fixture.ScrapeAntiforgeryTokenAsync("/help?edit");

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Key", key),
            new KeyValuePair<string, string>("BlockType", "Content"),
            new KeyValuePair<string, string>("Value", $"<p>e2e marker {unique}</p>"),
            new KeyValuePair<string, string>("OriginalValue", string.Empty),
            new KeyValuePair<string, string>("ReturnUrl", "/"),
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/content-block/save") { Content = form };
        request.Headers.Add("X-XSRF-TOKEN", token);
        request.Headers.Add("Cookie", cookie);

        var response = await client.SendAsync(request);

        // RED: deliberately wrong status — endpoint returns 302, this expects 200.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
