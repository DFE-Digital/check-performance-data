using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Web;

// The public media endpoint on the running app: anonymous, cached for a year, no session cookie,
// SVG sandboxed by its own content-security policy, and no resize without the server's signature.
// The seed endpoint returns the server-signed srcset, so a real signed rendition is fetched too.
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

    // Signed-in visitors send their auth cookie to a route that has no session.
    [Fact]
    public async Task ASeededPng_IsServedToASignedInEditorToo()
    {
        var media = await MediaSeedHelpers.SeedSampleAsync(fixture.SeedClient, "png");

        using var response = await fixture.SeedClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(fixture.SeedClient.BaseAddress!, media.Url)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(LongCache, response.Headers.CacheControl!.ToString());
    }

    // The sample is 64 pixels wide, so its srcset has one signed entry at 64w, never upscaled.
    [Fact]
    public async Task ASignedRendition_IsAWebP_AndTheSecondFetchMatches()
    {
        var media = await MediaSeedHelpers.SeedSampleAsync(fixture.SeedClient, "png");
        var entry = Assert.Single(media.SrcSet.Split(", "));
        var parts = entry.Split(' ');
        Assert.Equal("64w", parts[1]);
        Assert.Contains("sig=", parts[0]);

        using var first = await fixture.AnonymousClient.GetAsync(parts[0]);
        var bytes = await first.Content.ReadAsByteArrayAsync();
        using var second = await fixture.AnonymousClient.GetAsync(parts[0]);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("image/webp", first.Content.Headers.ContentType!.MediaType);
        Assert.Equal(LongCache, first.Headers.CacheControl!.ToString());
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
        Assert.Equal(64, WebPWidth(bytes));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(first.Headers.ETag, second.Headers.ETag);
        Assert.Equal(bytes, await second.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ASeededSvg_HasNoSrcSet()
    {
        var media = await MediaSeedHelpers.SeedSampleAsync(fixture.SeedClient, "svg");

        Assert.Equal("", media.SrcSet);
    }

    // Width from a lossy (VP8), lossless (VP8L) or extended (VP8X) WebP header.
    private static int WebPWidth(byte[] webp)
    {
        var chunk = System.Text.Encoding.ASCII.GetString(webp, 12, 4);
        return chunk switch
        {
            "VP8 " => (webp[26] | webp[27] << 8) & 0x3FFF,
            "VP8L" => 1 + (webp[21] | (webp[22] & 0x3F) << 8),
            "VP8X" => 1 + (webp[24] | webp[25] << 8 | webp[26] << 16),
            _ => throw new InvalidDataException($"Unknown WebP chunk {chunk}")
        };
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
