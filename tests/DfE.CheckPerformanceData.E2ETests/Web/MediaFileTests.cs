using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Web;

// The public media endpoint on the running app: anonymous, cached for a year, no session cookie,
// SVG sandboxed by its own content-security policy, and no resize without the server's signature.
// Signed renditions are covered by the integration tests, because only the server can sign.
[Trait("Category", "W1")]
[Collection("E2E")]
public sealed class MediaFileTests(PlaywrightFixture fixture)
{
    private const string LongCache = "public, max-age=31536000, immutable";
    private const string SvgPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";

    [Fact]
    public async Task ASeededPng_IsServedToAnAnonymousVisitor_WithLongCacheHeaders()
    {
        var media = await MediaSeedHelpers.SeedSampleAsync(fixture.SeedClient, "png");

        using var response = await fixture.AnonymousClient.GetAsync(media.Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(LongCache, response.Headers.CacheControl!.ToString());
        Assert.NotNull(response.Headers.ETag);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], bytes[..4]);
    }

    [Fact]
    public async Task ASeededSvg_IsServedWithItsOwnContentSecurityPolicy()
    {
        var media = await MediaSeedHelpers.SeedSampleAsync(fixture.SeedClient, "svg");

        using var response = await fixture.AnonymousClient.GetAsync(media.Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(SvgPolicy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(LongCache, response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task AnUnknownVersion_IsNotFound()
    {
        var media = await MediaSeedHelpers.SeedSampleAsync(fixture.SeedClient, "png");

        using var response = await fixture.AnonymousClient.GetAsync($"/media/{media.Id:D}/9999/sample.png");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("?w=640")]
    [InlineData("?w=640&sig=AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("?w=500&sig=AAAAAAAAAAAAAAAAAAAAAA")]
    public async Task AnUnsignedOrBadResize_IsRefused(string query)
    {
        var media = await MediaSeedHelpers.SeedSampleAsync(fixture.SeedClient, "png");

        using var response = await fixture.AnonymousClient.GetAsync(media.Url + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
