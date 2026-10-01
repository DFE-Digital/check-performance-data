using DfE.CheckPerformanceData.Application.SiteAssets;
using DfE.CheckPerformanceData.Web.ViewComponents;
using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.UnitTests.Web.ViewComponents;

public sealed class SiteAssetLinksTests
{
    private static readonly SiteAssetContent Both =
        new("h1{color:red}", "console.log(1)", true, true, "20260930153012", "20260101090000");

    private static QueryString Query(string q = "") => new(q);

    [Fact]
    public void PublicPage_GetsBothLinks_VersionedWithTheSaveTimestamp()
    {
        var links = SiteAssetLinks.For(Both, new PathString("/guidance/x"), Query());

        Assert.Equal("/cms/site.css?v=20260930153012", links.CssUrl);
        Assert.Equal("/cms/site.js?v=20260101090000", links.JsUrl);
    }

    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/settings")]
    [InlineData("/Admin/pages/5/edit")]
    public void AdminPages_GetTheAssetsToo(string path)
    {
        var links = SiteAssetLinks.For(Both, new PathString(path), Query());

        Assert.NotNull(links.CssUrl);
        Assert.NotNull(links.JsUrl);
    }

    [Theory]
    [InlineData("?siteAssets=off")]
    [InlineData("?x=1&siteAssets=OFF")]
    public void SafeMode_SuppressesBoth_ForThatRequestOnly(string query)
    {
        var links = SiteAssetLinks.For(Both, new PathString("/admin/site-assets"), Query(query));

        Assert.Null(links.CssUrl);
        Assert.Null(links.JsUrl);
    }

    [Fact]
    public void SwitchedOffOrEmptyAssets_AreNotLinked()
    {
        var links = SiteAssetLinks.For(new SiteAssetContent("h1{}", "", true, true, "20260930153012", ""), new PathString("/"), Query());
        Assert.NotNull(links.CssUrl);
        Assert.Null(links.JsUrl);

        var off = SiteAssetLinks.For(new SiteAssetContent("h1{}", "x", false, false), new PathString("/"), Query());
        Assert.Null(off.CssUrl);
        Assert.Null(off.JsUrl);
    }

    [Fact]
    public void ContentSavedBeforeTimestampsExisted_IsLinkedWithoutAVersion()
    {
        var links = SiteAssetLinks.For(new SiteAssetContent("h1{}", "x", true, true), new PathString("/"), Query());

        Assert.Equal("/cms/site.css", links.CssUrl);
        Assert.Equal("/cms/site.js", links.JsUrl);
    }

    // The site CSS link must come after ~/css/site.css so it can override anything in it.
    [Theory]
    [InlineData("_Layout.cshtml")]
    [InlineData("_AdminLayout.cshtml")]
    [InlineData("_ShareLayout.cshtml")]
    public void EveryMasterLayout_EmitsTheSiteAssetsComponent_AfterTheAppStylesheet(string layout)
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "Views", "Shared", layout));

        var appCss = src.IndexOf("~/css/site.css", StringComparison.Ordinal);
        var custom = src.IndexOf("Component.InvokeAsync(\"SiteAssets\")", StringComparison.Ordinal);

        Assert.True(appCss >= 0, "layout must load ~/css/site.css");
        Assert.True(custom > appCss, "site assets must be emitted after ~/css/site.css");
        Assert.DoesNotContain("<link rel=\"stylesheet\"", src[custom..], StringComparison.Ordinal);
    }

    [Fact]
    public void AdminWideLayout_InheritsTheAdminLayout()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "Views", "Shared", "_AdminWideLayout.cshtml"));

        Assert.Contains("Layout = \"_AdminLayout\"", src);
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "..", ".."));
}
