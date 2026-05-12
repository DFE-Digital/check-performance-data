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
}
