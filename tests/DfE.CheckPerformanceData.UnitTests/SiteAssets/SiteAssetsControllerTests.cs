using DfE.CheckPerformanceData.Application.SiteAssets;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using NSubstitute;

namespace DfE.CheckPerformanceData.UnitTests.SiteAssets;

public sealed class SiteAssetsControllerTests
{
    private readonly ISiteAssetService _assets = Substitute.For<ISiteAssetService>();
    private readonly SiteAssetsController _sut;

    public SiteAssetsControllerTests()
    {
        _sut = new SiteAssetsController(_assets)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private void Stored(string css = "", string js = "", bool cssOn = true, bool jsOn = true) =>
        _assets.GetAsync().Returns(new SiteAssetContent(css, js, cssOn, jsOn, SavedAt, SavedAt));

    private const string SavedAt = "20260930153012";

    [Fact]
    public async Task Css_ReturnsTheStoredCss_AsTextCss()
    {
        Stored(css: "h1{outline:2px solid red}");

        var result = Assert.IsType<ContentResult>(await _sut.Css(v: null));

        Assert.Equal("h1{outline:2px solid red}", result.Content);
        Assert.Equal("text/css; charset=utf-8", result.ContentType);
    }

    [Fact]
    public async Task Js_ReturnsTheStoredJs_AsJavaScript()
    {
        Stored(js: "console.log(1)");

        var result = Assert.IsType<ContentResult>(await _sut.Js(v: null));

        Assert.Equal("console.log(1)", result.Content);
        Assert.Equal("text/javascript; charset=utf-8", result.ContentType);
    }

    [Fact]
    public async Task Css_SwitchedOff_ReturnsAnEmptyStylesheet_SoAStaleLinkDoesNothing()
    {
        Stored(css: "h1{color:red}", cssOn: false);

        var result = Assert.IsType<ContentResult>(await _sut.Css(v: null));

        Assert.DoesNotContain("color", result.Content);
    }

    [Fact]
    public async Task Js_SwitchedOff_ReturnsAnEmptyScript()
    {
        Stored(js: "alert(1)", jsOn: false);

        var result = Assert.IsType<ContentResult>(await _sut.Js(v: null));

        Assert.DoesNotContain("alert", result.Content);
    }

    [Fact]
    public async Task Css_WithTheCurrentVersion_IsCachedForAYear()
    {
        Stored(css: "a{}");
        var version = SavedAt;

        await _sut.Css(version);

        var cache = _sut.Response.Headers[HeaderNames.CacheControl].ToString();
        Assert.Contains("max-age=31536000", cache);
        Assert.Contains("immutable", cache);
    }

    [Fact]
    public async Task Css_WithAStaleOrMissingVersion_IsNeverCached()
    {
        Stored(css: "a{}");

        await _sut.Css("20250101000000");

        Assert.Equal("no-cache", _sut.Response.Headers[HeaderNames.CacheControl].ToString());
    }

    [Fact]
    public async Task Css_SwitchedOff_IsNeverCached_EvenForTheVersionThatWasOnceServed()
    {
        Stored(css: "a{}", cssOn: false);
        var version = SavedAt;

        await _sut.Css(version);

        Assert.Equal("no-cache", _sut.Response.Headers[HeaderNames.CacheControl].ToString());
    }

    [Fact]
    public async Task Js_WithTheCurrentVersion_IsCachedForAYear()
    {
        Stored(js: "x=1");
        var version = SavedAt;

        await _sut.Js(version);

        Assert.Contains("max-age=31536000", _sut.Response.Headers[HeaderNames.CacheControl].ToString());
    }

    [Fact]
    public async Task NeverSavedAsset_IsNotCachedLong_EvenWhenTheRequestNamesNoVersion()
    {
        _assets.GetAsync().Returns(new SiteAssetContent("a{}", "", true, true));

        await _sut.Css("");

        Assert.Equal("no-cache", _sut.Response.Headers[HeaderNames.CacheControl].ToString());
    }

    [Fact]
    public void Endpoints_AreAnonymous_BecauseThePublicPagesLinkThem()
    {
        var type = typeof(SiteAssetsController);

        Assert.NotNull(type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), true).FirstOrDefault());
        Assert.Equal("cms/site.css", type.GetMethod(nameof(SiteAssetsController.Css))!.GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>().Single().Template);
        Assert.Equal("cms/site.js", type.GetMethod(nameof(SiteAssetsController.Js))!.GetCustomAttributes(typeof(HttpGetAttribute), false).Cast<HttpGetAttribute>().Single().Template);
    }
}
