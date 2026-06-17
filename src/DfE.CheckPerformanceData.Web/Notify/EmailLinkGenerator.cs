using DfE.CheckPerformanceData.Application.Notify;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Web.Notify;

public class EmailLinkGenerator(
    LinkGenerator linkGenerator,
    IHttpContextAccessor httpContextAccessor,
    IOptions<NotifySettings> notifySettings,
    ILogger<EmailLinkGenerator> logger) : IEmailLinkGenerator
{
    public string GenerateLink(string controller, string action, object? routeValues, string campaignName)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HttpContext is not available.");

        var url = BuildLinkUrl(httpContext, action, controller, routeValues);

        if (url is null)
            throw new InvalidOperationException(
                $"Route could not be resolved for controller '{controller}', action '{action}'.");

        url = StripExistingUtmParameters(url);
        url = AppendUtmParameters(url, campaignName);

        logger.LogDebug("Generated link \"{Url}\" for campaign \"{CampaignName}\"", url, campaignName);

        return url;
    }

    private string? BuildLinkUrl(HttpContext httpContext, string action, string controller, object? routeValues)
    {
        var linkBaseUrl = notifySettings.Value.LinkBaseUrl;

        if (!string.IsNullOrEmpty(linkBaseUrl))
        {
            if (Uri.TryCreate(linkBaseUrl, UriKind.Absolute, out var _))
            {
                var path = GetPathByActionCore(httpContext, action, controller, routeValues);
                return path is not null ? $"{linkBaseUrl.TrimEnd('/')}{path}" : null;
            }

            logger.LogWarning(
                "LinkBaseUrl \"{LinkBaseUrl}\" is not a valid absolute URI; falling back to HttpContext-based URL resolution",
                linkBaseUrl);
        }

        return GetUriByActionCore(httpContext, action, controller, routeValues);
    }

    private static string StripExistingUtmParameters(string url)
    {
        var uri = new Uri(url);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var keysToRemove = query.AllKeys
            .Where(k => k?.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        if (keysToRemove.Count == 0)
            return url;

        foreach (var key in keysToRemove)
            query.Remove(key);

        var builder = new UriBuilder(uri);
        builder.Query = query.ToString() ?? "";
        return builder.Uri.ToString();
    }

    private string AppendUtmParameters(string url, string campaignName)
    {
        var settings = notifySettings.Value;

        if (!string.IsNullOrEmpty(settings.UtmSource))
            url = QueryHelpers.AddQueryString(url, "utm_source", settings.UtmSource);

        if (!string.IsNullOrEmpty(settings.UtmMedium))
            url = QueryHelpers.AddQueryString(url, "utm_medium", settings.UtmMedium);

        if (!string.IsNullOrEmpty(campaignName) && settings.UtmCampaigns?.TryGetValue(campaignName, out var campaign) == true)
            url = QueryHelpers.AddQueryString(url, "utm_campaign", campaign);
        else if (!string.IsNullOrEmpty(campaignName))
            url = QueryHelpers.AddQueryString(url, "utm_campaign", campaignName);

        return url;
    }

    protected virtual string? GetUriByActionCore(HttpContext httpContext, string action, string controller, object? routeValues)
    {
        return linkGenerator.GetUriByAction(httpContext, action, controller, routeValues);
    }

    protected virtual string? GetPathByActionCore(HttpContext httpContext, string action, string controller, object? routeValues)
    {
        return linkGenerator.GetPathByAction(httpContext, action, controller, routeValues);
    }
}
