using System.Net;
using System.Net.Http.Headers;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Upload;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Infrastructure.Media;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Extensions;
using DfE.CheckPerformanceData.Web.Middleware;
using DfE.CheckPerformanceData.Web.Startup;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace DfE.CheckPerformanceData.IntegrationTests.Media;

// The public media endpoint through the parts of the site pipeline that touch it: the security
// headers, the session, the not-found page and the sign-in requirement. Files are stored by the
// real upload pipeline in Azurite and Postgres, and renditions are made by the real renderer.
[Collection(nameof(PostgresCollection))]
public sealed class MediaFileEndpointTests(PostgresFixture postgres, AzuriteFixture azurite)
    : IClassFixture<AzuriteFixture>, IAsyncLifetime
{
    private const string LongCache = "public, max-age=31536000, immutable";
    private const string SvgPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
    private const string NotFoundPage = "<html>the site not-found page</html>";

    private readonly BlobServiceClient _blobService = new(azurite.ConnectionString);
    private readonly string _container = $"media-{Guid.NewGuid():N}";
    private readonly CountingRenderer _renderer = new();
    private readonly MediaUrlSigner _signer = new(new FixedKey());
    private IHost _host = null!;
    private HttpClient _client = null!;

    private MediaBlobStore Blobs => new(_blobService, _container);

    public async Task InitializeAsync()
    {
        await using (var ctx = postgres.CreateContext())
            await ctx.Database.ExecuteSqlRawAsync("TRUNCATE media_usages, media_asset_versions, media_assets");

        _host = await new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices((ctx, services) =>
            {
                services.AddLogging();
                services.AddDistributedMemoryCache();
                services.AddCpdSession(ctx.Configuration, ctx.HostingEnvironment);
                services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
                services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
                services.AddScoped<IMediaAssetRepository>(_ => new MediaAssetRepository(postgres.CreateContext(), new FakeCurrentUserService()));
                services.AddSingleton<IMediaBlobStore>(Blobs);
                services.AddSingleton<IMediaRenditionRenderer>(_renderer);
                services.AddSingleton(new MediaProcessingGate(Options.Create(new MediaProcessingOptions())));
                services.AddSingleton(_signer);
                services.AddScoped<IMediaDeliveryService, MediaDeliveryService>();
                services.AddControllers().AddApplicationPart(typeof(MediaFileController).Assembly);
            });
            web.Configure(app =>
            {
                app.UseMiddleware<SecurityHeadersMiddleware>();
                app.UseCpdSession();
                app.UseRouting();
                app.UseStatusCodePagesWithReExecute("/Home/NotFound");
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(e =>
                {
                    e.MapControllers();
                    e.MapGet("/Home/NotFound", () => Results.Content(NotFoundPage, "text/html")).AllowAnonymous();
                    e.MapGet("/signed-in-only", () => "ok");
                });
            });
        }).StartAsync();
        _client = _host.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _host.Dispose();
        await Task.CompletedTask;
    }

    private async Task<(MediaAssetDto Asset, MediaUrlBuilder Urls)> UploadAsync(byte[] bytes, string name)
    {
        await using var ctx = postgres.CreateContext();
        var service = new MediaUploadService(
            new MediaFileCleaner(new SkiaRasterImageCleaner()),
            new MediaAssetRepository(ctx, new FakeCurrentUserService()),
            Blobs,
            new MediaProcessingGate(Options.Create(new MediaProcessingOptions())),
            NullLogger<MediaUploadService>.Instance);
        var result = await service.UploadAsync(new MemoryStream(bytes), name);
        Assert.Equal(MediaUploadOutcome.Created, result.Outcome);
        return (result.Asset!, new MediaUrlBuilder(_signer));
    }

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.SeaGreen);
        bitmap.SetPixel(0, 0, new SKColor((byte)Random.Shared.Next(256), (byte)Random.Shared.Next(256), 0));
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data!.ToArray();
    }

    private static byte[] Svg() => System.Text.Encoding.UTF8.GetBytes(
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"40\" height=\"20\"><rect width=\"{Random.Shared.Next(1, 40)}\" height=\"20\" fill=\"#1d70b8\"/></svg>");

    private static (int Width, int Height, SKEncodedImageFormat Format) Measure(byte[] bytes)
    {
        using var codec = SKCodec.Create(new MemoryStream(bytes));
        return (codec.Info.Width, codec.Info.Height, codec.EncodedFormat);
    }

    [Fact]
    public async Task TheOriginal_IsServedAnonymously_WithLongCacheHeaders()
    {
        var png = Png(30, 20);
        var (asset, urls) = await UploadAsync(png, "Playground.png");

        using var response = await _client.GetAsync(urls.Original(asset, asset.Latest));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(LongCache, response.Headers.CacheControl!.ToString());
        Assert.Equal($"\"{asset.Id:N}.1\"", response.Headers.ETag!.Tag);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal((30, 20, SKEncodedImageFormat.Png), Measure(await response.Content.ReadAsByteArrayAsync()));
    }

    [Fact]
    public async Task TheHostRequiresSignIn_ElsewhereThanMedia()
    {
        using var response = await _client.GetAsync("/signed-in-only");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task ARepeatRequestWithTheETag_GetsNotModified()
    {
        var (asset, urls) = await UploadAsync(Png(30, 20), "a.png");
        using var request = new HttpRequestMessage(HttpMethod.Get, urls.Original(asset, asset.Latest));
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue($"\"{asset.Id:N}.1\""));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
    }

    // The slug and extension are for people; the id and version decide what is served.
    [Fact]
    public async Task ADifferentSlugOrExtension_ServesTheSameFile()
    {
        var (asset, _) = await UploadAsync(Png(30, 20), "a.png");

        using var response = await _client.GetAsync($"/media/{asset.Id:D}/1/renamed.gif");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData("/media/{0}/2/a.png")]
    [InlineData("/media/00000000-0000-0000-0000-000000000001/1/a.png")]
    [InlineData("/media/{0}/0/a.png")]
    [InlineData("/media/not-a-guid/1/a.png")]
    [InlineData("/media/{0}/1")]
    [InlineData("/media/{0}/1/a.png/extra")]
    [InlineData("/media")]
    [InlineData("/media/anything")]
    public async Task AnUnknownImageOrVersion_IsABare404(string template)
    {
        var (asset, _) = await UploadAsync(Png(30, 20), "a.png");

        using var response = await _client.GetAsync(string.Format(template, asset.Id.ToString("D")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("not-found page", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ADeletedImage_IsNotFound()
    {
        var (asset, urls) = await UploadAsync(Png(30, 20), "a.png");
        await using (var ctx = postgres.CreateContext())
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM media_assets WHERE id = {asset.Id}");

        using var response = await _client.GetAsync(urls.Original(asset, asset.Latest));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnSvg_IsServedWithItsOwnContentSecurityPolicy()
    {
        var (asset, urls) = await UploadAsync(Svg(), "logo.svg");

        using var response = await _client.GetAsync(urls.Original(asset, asset.Latest));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(SvgPolicy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(LongCache, response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task ASignedRenditionOfAnSvg_IsTheSvg()
    {
        var (asset, urls) = await UploadAsync(Svg(), "logo.svg");
        var signed = $"/media/{asset.Id:D}/1/logo.webp?{MediaRenditionQuery.WithSignature(new MediaRendition(320, null, null, null), _signer.Sign(asset.Id, 1, new MediaRendition(320, null, null, null)))}";

        using var response = await _client.GetAsync(signed);

        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(SvgPolicy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal(0, _renderer.Calls);
    }

    [Fact]
    public async Task ASignedRendition_IsAWebPAtTheWidth_AndIsStoredForTheNextRequest()
    {
        var (asset, urls) = await UploadAsync(Png(1000, 500), "wide.png");
        var url = urls.Rendition(asset, asset.Latest, 640);

        using var first = await _client.GetAsync(url);
        var firstBytes = await first.Content.ReadAsByteArrayAsync();
        using var second = await _client.GetAsync(url);
        var secondBytes = await second.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("image/webp", first.Content.Headers.ContentType!.MediaType);
        Assert.Equal(LongCache, first.Headers.CacheControl!.ToString());
        Assert.Equal((640, 320, SKEncodedImageFormat.Webp), Measure(firstBytes));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(firstBytes, secondBytes);
        Assert.Equal(1, _renderer.Calls);
        var hash = MediaRenditionQuery.Hash(new MediaRendition(640, null, null, null));
        Assert.True(await Blobs.ExistsAsync(MediaBlobKeys.Rendition(asset.Id, 1, hash)));
        Assert.Equal(first.Headers.ETag, second.Headers.ETag);
        Assert.NotEqual($"\"{asset.Id:N}.1\"", first.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task ARendition_IsNeverLargerThanTheImage()
    {
        var (asset, urls) = await UploadAsync(Png(400, 300), "small.png");

        using var response = await _client.GetAsync(urls.Rendition(asset, asset.Latest, 1920));

        Assert.Equal((400, 300, SKEncodedImageFormat.Webp), Measure(await response.Content.ReadAsByteArrayAsync()));
    }

    [Fact]
    public async Task ACroppedRendition_HasTheAspectAsked()
    {
        var (asset, urls) = await UploadAsync(Png(1000, 1000), "square.png");

        using var response = await _client.GetAsync(urls.Rendition(asset, asset.Latest, 320,
            new MediaCrop(0, 0, 1, 0.8m), new MediaAspectRatio(16, 9), new MediaFocalPoint(0.2m, 0.2m)));

        Assert.Equal((320, 180, SKEncodedImageFormat.Webp), Measure(await response.Content.ReadAsByteArrayAsync()));
    }

    [Fact]
    public async Task TwoFirstRequestsAtOnce_BothSucceed()
    {
        var (asset, urls) = await UploadAsync(Png(1200, 600), "race.png");
        var url = urls.Rendition(asset, asset.Latest, 960);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => _client.GetAsync(url)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        foreach (var r in responses)
            Assert.Equal((960, 480, SKEncodedImageFormat.Webp), Measure(await r.Content.ReadAsByteArrayAsync()));
    }

    [Theory]
    [InlineData("?w=640")]
    [InlineData("?w=640&sig=AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("?w=500&sig=AAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("?crop=0,0,0.5,0.5")]
    [InlineData("?ar=16:9")]
    public async Task AnUnsignedOrBadRendition_IsRefused(string query)
    {
        var (asset, _) = await UploadAsync(Png(1000, 500), "a.png");

        using var response = await _client.GetAsync($"/media/{asset.Id:D}/1/a.webp{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _renderer.Calls);
    }

    [Fact]
    public async Task AWidthChangedAfterSigning_IsRefused()
    {
        var (asset, urls) = await UploadAsync(Png(1000, 500), "a.png");
        var url = urls.Rendition(asset, asset.Latest, 320).Replace("w=320", "w=1920");

        using var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class FixedKey : IMediaUrlSigningKey
    {
        private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        public byte[] GetKey() => Key;
    }

    private sealed class CountingRenderer : IMediaRenditionRenderer
    {
        private readonly SkiaMediaRenditionRenderer _inner = new();
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public byte[] Render(byte[] source, MediaRenditionPlan plan)
        {
            Interlocked.Increment(ref _calls);
            return _inner.Render(source, plan);
        }
    }
}
