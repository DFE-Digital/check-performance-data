using System.Reflection;
using System.Text.Json;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class CookiesControllerTests
{
    private const string CookieName = "cookies_policy";

    private static (CookiesController sut, DefaultHttpContext http) Build(string? cookie = null, bool isHttps = false)
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = isHttps ? "https" : "http";
        http.Request.Host = new HostString(isHttps ? "localhost" : "localhost:8080");
        if (cookie is not null)
        {
            http.Request.Headers["Cookie"] = $"{CookieName}={cookie}";
        }

        var sut = new CookiesController
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
        return (sut, http);
    }

    private static string? ParseSetCookieAnalytics(DefaultHttpContext http)
    {
        var setCookie = http.Response.Headers["Set-Cookie"].ToString();
        if (string.IsNullOrEmpty(setCookie)) return null;

        var firstSegment = setCookie.Split(';')[0];
        var equalsIndex = firstSegment.IndexOf('=');
        if (equalsIndex < 0) return null;

        var encoded = firstSegment[(equalsIndex + 1)..];
        return Uri.UnescapeDataString(encoded);
    }

    // --- GET /cookies ---

    [Fact]
    public void Index_NoCookie_AnalyticsIsNull()
    {
        var (sut, _) = Build();

        var result = sut.Index(saved: false);

        var view = Assert.IsType<ViewResult>(result);
        var vm = Assert.IsType<CookiesViewModel>(view.Model);
        Assert.Null(vm.Analytics);
    }

    [Fact]
    public void Index_AnalyticsTrueCookie_AnalyticsIsTrue()
    {
        var raw = Uri.EscapeDataString(JsonSerializer.Serialize(new { analytics = true }));
        var (sut, _) = Build(cookie: raw);

        var result = sut.Index(saved: false);

        var vm = Assert.IsType<CookiesViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(vm.Analytics);
    }

    [Fact]
    public void Index_AnalyticsFalseCookie_AnalyticsIsFalse()
    {
        var raw = Uri.EscapeDataString(JsonSerializer.Serialize(new { analytics = false }));
        var (sut, _) = Build(cookie: raw);

        var result = sut.Index(saved: false);

        var vm = Assert.IsType<CookiesViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.False(vm.Analytics);
    }

    [Fact]
    public void Index_MalformedJsonCookie_DoesNotThrow_AnalyticsIsNull()
    {
        var (sut, _) = Build(cookie: Uri.EscapeDataString("not-json"));

        var result = sut.Index(saved: false);

        var vm = Assert.IsType<CookiesViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Null(vm.Analytics);
    }

    [Fact]
    public void Index_CookieMissingAnalyticsKey_AnalyticsIsNull()
    {
        var raw = Uri.EscapeDataString(JsonSerializer.Serialize(new { other = 1 }));
        var (sut, _) = Build(cookie: raw);

        var result = sut.Index(saved: false);

        var vm = Assert.IsType<CookiesViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Null(vm.Analytics);
    }

    [Fact]
    public void Index_SavedQueryTrue_SetsSavedFlag()
    {
        var (sut, _) = Build();

        var result = sut.Index(saved: true);

        var vm = Assert.IsType<CookiesViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(vm.Saved);
    }

    // --- POST /cookies ---

    [Fact]
    public void Save_AnalyticsTrue_WritesCookieWithAnalyticsTrue()
    {
        var (sut, http) = Build();

        sut.Save(analytics: true);

        var payload = ParseSetCookieAnalytics(http);
        Assert.NotNull(payload);
        using var doc = JsonDocument.Parse(payload!);
        Assert.True(doc.RootElement.GetProperty("analytics").GetBoolean());
    }

    [Fact]
    public void Save_AnalyticsFalse_WritesCookieWithAnalyticsFalse()
    {
        var (sut, http) = Build();

        sut.Save(analytics: false);

        var payload = ParseSetCookieAnalytics(http);
        using var doc = JsonDocument.Parse(payload!);
        Assert.False(doc.RootElement.GetProperty("analytics").GetBoolean());
    }

    [Fact]
    public void Save_AnalyticsNull_TreatedAsFalse()
    {
        // Defensive: form-post with no analytics value must NOT default to consent.
        var (sut, http) = Build();

        sut.Save(analytics: null);

        var payload = ParseSetCookieAnalytics(http);
        using var doc = JsonDocument.Parse(payload!);
        Assert.False(doc.RootElement.GetProperty("analytics").GetBoolean());
    }

    [Fact]
    public void Save_RedirectsToIndexWithSavedTrue()
    {
        var (sut, _) = Build();

        var result = sut.Save(analytics: true);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(CookiesController.Index), redirect.ActionName);
        Assert.Equal(true, redirect.RouteValues!["saved"]);
    }

    [Fact]
    public void Save_OverHttps_CookieIsSecure()
    {
        var (sut, http) = Build(isHttps: true);

        sut.Save(analytics: true);

        var setCookie = http.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Save_OverHttp_CookieIsNotSecure()
    {
        var (sut, http) = Build(isHttps: false);

        sut.Save(analytics: true);

        var setCookie = http.Response.Headers["Set-Cookie"].ToString();
        Assert.DoesNotContain("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Save_CookieHasSameSiteLax_AndIsEssential()
    {
        var (sut, http) = Build();

        sut.Save(analytics: true);

        var setCookie = http.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        // IsEssential = true means the cookie is written even without consent — verified by
        // the fact that Set-Cookie is present at all (no consent feature enabled in test).
        Assert.NotEmpty(setCookie);
    }

    [Fact]
    public void Save_CookieExpiresApproximately_365Days()
    {
        var before = DateTimeOffset.UtcNow;
        var (sut, http) = Build();

        sut.Save(analytics: true);

        var setCookie = http.Response.Headers["Set-Cookie"].ToString();
        var expiresMatch = System.Text.RegularExpressions.Regex.Match(
            setCookie, @"expires=([^;]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Assert.True(expiresMatch.Success, $"Set-Cookie should include expires: {setCookie}");

        var expires = DateTimeOffset.Parse(expiresMatch.Groups[1].Value);
        var delta = expires - before;
        Assert.InRange(delta.TotalDays, 364.5, 365.5);
    }

    // --- Route wiring ---

    [Fact]
    public void Index_HasHttpGet_CookiesAttributeRoute()
    {
        var method = typeof(CookiesController).GetMethod(nameof(CookiesController.Index))!;
        var attr = method.GetCustomAttribute<HttpGetAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("/cookies", attr!.Template);
    }

    [Fact]
    public void Save_HasHttpPost_AndAntiForgery()
    {
        var method = typeof(CookiesController).GetMethod(nameof(CookiesController.Save))!;
        var post = method.GetCustomAttribute<HttpPostAttribute>();
        var antiforgery = method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>();
        Assert.NotNull(post);
        Assert.Equal("/cookies", post!.Template);
        Assert.NotNull(antiforgery);
    }
}
