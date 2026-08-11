using DfE.CheckPerformanceData.Application.Analytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

/// <summary>
/// Beacon endpoint for client-side analytics (AB#286387 R18/R19/R23).
/// Allowlisted event names only; the server constructs the typed event so
/// clients cannot inject arbitrary fields. page_path comes from the Referer
/// path — its query string is dropped because it can carry search terms.
/// </summary>
[AllowAnonymous]
public sealed class ClientEventsController(IAnalyticsService analytics) : Controller
{
    private const int MaxTextLength = 100;

    public sealed class ClientEventRequest
    {
        public string? EventName { get; init; }
        public string? ExpandText { get; init; }
        public string? Destination { get; init; }
    }

    [HttpPost]
    [Route("/client-events")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Post([FromBody] ClientEventRequest? request)
    {
        var pagePath = RefererPath();

        AnalyticsEvent? analyticsEvent = request?.EventName switch
        {
            "help_details_expanded" => new HelpDetailsExpandedEvent
            {
                ExpandText = Truncate(request.ExpandText),
                PagePath = pagePath,
            },
            "external_link_clicked" when !string.IsNullOrWhiteSpace(request.Destination) =>
                new ExternalLinkClickedEvent
                {
                    Destination = MapDestination(request.Destination),
                    PagePath = pagePath,
                },
            "evidence_file_selected" => new EvidenceFileSelectedEvent { PagePath = pagePath },
            _ => null,
        };

        if (analyticsEvent is null)
        {
            return BadRequest();
        }

        await analytics.TrackSafeAsync(analyticsEvent, HttpContext.RequestAborted);
        return NoContent();
    }

    private string? RefererPath()
        => Uri.TryCreate(Request.Headers.Referer.ToString(), UriKind.Absolute, out var referer)
            ? referer.AbsolutePath
            : null;

    private static string MapDestination(string hostname)
    {
        var host = hostname.Trim().ToLowerInvariant();
        return host is "get-information-schools.service.gov.uk" or "www.get-information-schools.service.gov.uk"
            ? "gias"
            : Truncate(host)!;
    }

    private static string? Truncate(string? value)
        => value is { Length: > MaxTextLength } ? value[..MaxTextLength] : value;
}
