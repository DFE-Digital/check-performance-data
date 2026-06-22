using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Web.Extensions;

/// <summary>
/// Reads the user's analytics consent from the <c>cookies_policy</c> cookie.
/// Analytics tags (GTM, Microsoft Clarity) are only loaded when this returns true
/// (basic consent mode), so nothing is sent to those services without consent.
/// Fails closed: a missing, malformed, or unexpected cookie is treated as no consent.
/// </summary>
public static class AnalyticsConsentExtensions
{
    private const string CookieName = "cookies_policy";

    public static bool IsAnalyticsConsentGranted(this HttpContext context)
    {
        var raw = context.Request.Cookies[CookieName];
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.GetProperty("analytics").GetBoolean();
        }
        catch
        {
            return false;
        }
    }
}
