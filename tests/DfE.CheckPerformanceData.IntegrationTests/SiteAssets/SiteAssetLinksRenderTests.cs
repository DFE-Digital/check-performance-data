using DfE.CheckPerformanceData.Application.SiteAssets;
using DfE.CheckPerformanceData.Web.ViewComponents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.IntegrationTests.SiteAssets;

// Renders the real site-assets view component, through the compiled Razor view the layouts use,
// at different request paths. The page that edits the assets must never load them, so a broken
// script or rule cannot stop an administrator from reaching the page that fixes it.
public sealed class SiteAssetLinksRenderTests
{
    private static readonly SiteAssetContent Content =
        new("body{outline:3px solid #d4351c}", "document.documentElement.dataset.siteJs='1'", true, true,
            "20260930153012", "20260930153012");

    [Theory]
    [InlineData("/admin/site-assets")]
    [InlineData("/Admin/Site-Assets")]
    [InlineData("/admin/site-assets/")]
    public async Task TheEditorPage_NeverLinksTheAssets(string path)
    {
        var html = await RenderAtAsync(path);

        Assert.DoesNotContain("/cms/site.css", html);
        Assert.DoesNotContain("/cms/site.js", html);
    }

    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/settings")]
    [InlineData("/admin/site-assets-help")]
    [InlineData("/guidance")]
    [InlineData("/share/abc")]
    public async Task OtherPages_IncludingOtherAdminPages_LinkBothAssets(string path)
    {
        var html = await RenderAtAsync(path);

        Assert.Contains("<link rel=\"stylesheet\" href=\"/cms/site.css?v=20260930153012\" />", html);
        Assert.Contains("<script src=\"/cms/site.js?v=20260930153012\" defer></script>", html);
    }

    private static async Task<string> RenderAtAsync(string path)
    {
        using var host = await new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddSingleton<ISiteAssetService, StubAssets>();
                services.AddControllersWithViews().ConfigureApplicationPartManager(parts =>
                {
                    // The web assembly supplies the view component and its compiled view; only this
                    // test's controller is routed, so none of the site's own controllers are needed.
                    var web = typeof(SiteAssetsViewComponent).Assembly;
                    foreach (var part in ApplicationPartFactory.GetApplicationPartFactory(web).GetApplicationParts(web))
                        parts.ApplicationParts.Add(part);
                    parts.ApplicationParts.Add(new AssemblyPart(typeof(RenderController).Assembly));
                    parts.FeatureProviders.Remove(parts.FeatureProviders.OfType<ControllerFeatureProvider>().Single());
                    parts.FeatureProviders.Add(new OnlyRenderController());
                });
            });
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(e => e.MapControllers());
            });
        }).StartAsync();

        using var response = await host.GetTestClient().GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public sealed class RenderController : Controller
    {
        [HttpGet("{**path}")]
        public IActionResult Render() => ViewComponent("SiteAssets");
    }

    private sealed class OnlyRenderController : ControllerFeatureProvider
    {
        protected override bool IsController(System.Reflection.TypeInfo typeInfo) =>
            typeInfo.AsType() == typeof(RenderController);
    }

    private sealed class StubAssets : ISiteAssetService
    {
        public Task<SiteAssetContent> GetAsync() => Task.FromResult(Content);
        public Task<SiteAssetSaveResult> SaveAsync(SiteAssetContent content) => Task.FromResult(new SiteAssetSaveResult(true));
    }
}
