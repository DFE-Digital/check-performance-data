using System.Net;
using DfE.CheckPerformanceData.Application.SiteAssets;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Extensions;
using DfE.CheckPerformanceData.Web.Startup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.IntegrationTests.SiteAssets;

// The versioned site asset URLs are immutable, so they must reach the browser with the long
// cache header and without a session cookie. Runs through the same session setup the site uses.
public sealed class SiteAssetCachingTests
{
    private const string LongCache = "public, max-age=31536000, immutable";

    private static readonly SiteAssetContent Content = new("a{}", "1;", true, true);

    public static TheoryData<string> VersionedUrls => new()
    {
        $"/cms/site.css?v={Content.CssVersion}",
        $"/cms/site.js?v={Content.JsVersion}",
    };

    [Theory]
    [MemberData(nameof(VersionedUrls))]
    public async Task VersionedRequest_GetsLongCache_AndNoSessionCookie(string url)
    {
        using var host = await BuildHostAsync();

        using var response = await host.GetTestClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(LongCache, response.Headers.CacheControl!.ToString());
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("/cms/site.css")]
    [InlineData("/cms/site.css?v=stale")]
    [InlineData("/cms/site.js?v=stale")]
    public async Task UnversionedOrStaleRequest_IsNotCachedLong(string url)
    {
        using var host = await BuildHostAsync();

        using var response = await host.GetTestClient().GetAsync(url);

        Assert.Equal("no-cache", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task OtherPaths_StillGetASession()
    {
        using var host = await BuildHostAsync();

        using var response = await host.GetTestClient().GetAsync("/other");

        Assert.Contains(".AspNetCore.Session", string.Join(";", response.Headers.GetValues("Set-Cookie")));
    }

    private static async Task<IHost> BuildHostAsync() =>
        await new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices((ctx, services) =>
            {
                services.AddDistributedMemoryCache();
                services.AddCpdSession(ctx.Configuration, ctx.HostingEnvironment);
                services.AddSingleton<ISiteAssetService, StubAssets>();
                services.AddControllers().AddApplicationPart(typeof(SiteAssetsController).Assembly);
            });
            web.Configure(app =>
            {
                app.UseCpdSession();
                app.UseRouting();
                app.UseEndpoints(e =>
                {
                    e.MapControllers();
                    e.MapGet("/other", () => "ok");
                });
            });
        }).StartAsync();

    private sealed class StubAssets : ISiteAssetService
    {
        public Task<SiteAssetContent> GetAsync() => Task.FromResult(Content);
        public Task<SiteAssetSaveResult> SaveAsync(SiteAssetContent content) => Task.FromResult(new SiteAssetSaveResult(true));
    }
}
