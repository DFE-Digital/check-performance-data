using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;

namespace DfE.CheckPerformanceData.E2ETests.Web;

[Collection("E2E")]
[Trait("Category", "W4")]
public sealed class WikiCrudTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    private HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        return new HttpClient(handler) { BaseAddress = new Uri(_fixture.BaseUrl) };
    }

    // --- PostHelpCreate_RedirectsToSlug ---

    [Fact]
    public async Task PostHelpCreate_RedirectsToSlug()
    {
        var unique = $"e2e-{Guid.NewGuid():N}";
        var title = $"{unique}-create-target";

        using var client = CreateClient();
        var (token, cookie) = await _fixture.ScrapeAntiforgeryTokenAsync("/help?edit");

        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Title", title),
            new KeyValuePair<string, string>("Content", "Body content for create-target."),
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/help/create") { Content = form };
        request.Headers.Add("X-XSRF-TOKEN", token);
        request.Headers.Add("Cookie", cookie);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var location = response.Headers.Location!.ToString();
        Assert.Contains($"/help/{title.ToLowerInvariant()}", location);
    }
}
