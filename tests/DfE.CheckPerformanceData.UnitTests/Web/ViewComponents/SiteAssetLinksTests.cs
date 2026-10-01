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
    [InlineData("/ADMIN/site-assets")]
    public void AdminPages_NeverGetTheAssets_SoABadScriptCannotLockAdminsOut(string path)
    {
        var links = SiteAssetLinks.For(Both, new PathString(path), Query());

        Assert.Null(links.CssUrl);
        Assert.Null(links.JsUrl);
    }

    [Fact]
    public void PathMerelyStartingWithAdmin_IsStillAPublicPage()
    {
        var links = SiteAssetLinks.For(Both, new PathString("/administration-guidance"), Query());

        Assert.NotNull(links.CssUrl);
    }

    [Theory]
    [InlineData("?siteAssets=off")]
    [InlineData("?x=1&siteAssets=OFF")]
    public void SafeMode_SuppressesBoth_ForThatRequestOnly(string query)
    {
        var links = SiteAssetLinks.For(Both, new PathString("/guidance"), Query(query));

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

    [Fact]
    public void PublicLayout_InvokesTheComponent_AndTheAdminLayoutDoesNot()
    {
        var views = Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "Views", "Shared");

        Assert.Contains("Component.InvokeAsync(\"SiteAssets\")", File.ReadAllText(Path.Combine(views, "_Layout.cshtml")));
        Assert.DoesNotContain("SiteAssets", File.ReadAllText(Path.Combine(views, "_AdminLayout.cshtml")));
        Assert.DoesNotContain("SiteAssets", File.ReadAllText(Path.Combine(views, "_AdminWideLayout.cshtml")));
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "..", ".."));
}
