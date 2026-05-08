using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace DfE.CheckPerformanceData.Web.Controllers;

[AllowAnonymous]
public sealed class CookiesController : Controller
{
    private const string CookieName = "cookies_policy";
    private const int CookieExpiryDays = 365;

    [HttpGet("/cookies")]
    public IActionResult Index([FromQuery] bool saved = false)
    {
        var vm = new CookiesViewModel
        {
            Analytics = ReadAnalyticsConsent(),
            Saved = saved
        };
        return View(vm);
    }

    [HttpPost("/cookies")]
    [ValidateAntiForgeryToken]
    public IActionResult Save(bool? analytics)
    {
        var accepted = analytics == true;

        Response.Cookies.Append(CookieName, JsonSerializer.Serialize(new { analytics = accepted }), new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddDays(CookieExpiryDays),
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            Path = "/"
        });

        return RedirectToAction(nameof(Index), new { saved = true });
    }

    private bool? ReadAnalyticsConsent()
    {
        var raw = Request.Cookies[CookieName];
        if (raw is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.GetProperty("analytics").GetBoolean();
        }
        catch
        {
            return null;
        }
    }
}
