using System.Text.Json;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Where an instant search reports itself once the person has settled on a query.
//
// A typeahead has no server-side moment to record. The server sees a suggestion fetch per
// settled keystroke and nothing at all for an on-page search, and neither of those is the
// event worth keeping: what matters is the query someone ended on, what they were shown for
// it, and whether they took any of it. Only the browser knows that, so it reports it — once
// per settled query, never per keystroke.
//
// Everything in the payload is therefore untrusted input, and is treated as such: the surface
// must be one this app declares, the query is sliced to the same 100 characters /search uses,
// the result list is capped, an unrecognised result kind degrades to "page" rather than being
// written through, and a malformed report is refused rather than persisted half-formed.
//
// The session id is NOT taken from the payload. It comes from the server-side session, the
// same way the feedback form does, so a report cannot file itself against someone else's
// history.
//
// Load: the reports land on the same bounded channel as every other analytics event, which
// sheds and counts drops when full. A flood costs dropped rows and a warn line, not the app.
[AllowAnonymous]
public sealed class InstantSearchAnalyticsController(ISearchTelemetry telemetry) : Controller
{
    private const int MaxQueryLength = 100;
    private const int MinQueryLength = 2;
    private const int MaxShown = 10;
    private const int MaxKeyLength = 512;
    private const int MaxLabelLength = 256;
    private const int MaxLatencyMs = 60_000;

    // Form-encoded rather than JSON because the browser sends this with sendBeacon, which
    // survives the page being closed but cannot set headers — so the antiforgery token has to
    // travel in the body, which means a form content type. The result list is one JSON field
    // inside that form.
    [HttpPost("/search/instant-analytics")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Record([FromForm] InstantSearchReport report, CancellationToken ct)
    {
        if (!SearchSurfaces.IsInstant(report.Surface))
        {
            return BadRequest();
        }

        var query = (report.Q ?? string.Empty).Trim();
        if (query.Length > MaxQueryLength) query = query[..MaxQueryLength];

        // Below the minimum the widget never ran a search, so there is nothing to record.
        // Not an error — the browser is allowed to report a query the person then deleted.
        if (query.Length < MinQueryLength)
        {
            return NoContent();
        }

        if (!TryReadShown(report.Shown, out var shown))
        {
            return BadRequest();
        }

        // A position outside the menu that was actually shown is nonsense, and a selection is
        // only meaningful alongside its key — so an out-of-range one is dropped rather than
        // stored as a number no chart can interpret.
        var selectedPosition = report.SelectedPosition is { } p && p >= 1 && p <= shown.Count
            ? p
            : (int?)null;

        var selectedKey = Trim(report.SelectedKey, MaxKeyLength);

        await HttpContext.Session.LoadAsync(ct);
        CpdSessionIdentity.Ensure(HttpContext.Session);

        telemetry.RecordInstantSearch(new InstantSearchTelemetryEvent(
            SearchId: Guid.NewGuid(),
            UtcTimestamp: DateTime.UtcNow,
            QueryRaw: query,
            QueryNormalised: SearchTermNormalizer.OrJoinWhitespace(query),
            Scope: Trim(report.Scope, MaxKeyLength),
            Surface: report.Surface!,
            // A host page only means something for an on-page search. Ignoring it elsewhere
            // keeps the single-page section of the dashboard from filling with site searches.
            HostPath: report.Surface == SearchSurfaces.InstantPage
                ? Trim(report.HostPath, MaxKeyLength)
                : null,
            LatencyMs: Math.Clamp(report.LatencyMs, 0, MaxLatencyMs),
            Shown: shown,
            SelectedKey: selectedKey,
            SelectedPosition: selectedKey is null ? null : selectedPosition));

        return NoContent();
    }

    private static bool TryReadShown(string? json, out IReadOnlyList<InstantSearchShownHit> shown)
    {
        shown = [];
        if (string.IsNullOrWhiteSpace(json)) return true;

        List<ShownDto>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<List<ShownDto>>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // A report we cannot read is refused outright. Writing the parent row without its
            // results would look like a zero-result search, which is a different fact.
            return false;
        }

        if (parsed is null) return true;

        var hits = new List<InstantSearchShownHit>(Math.Min(parsed.Count, MaxShown));
        for (var i = 0; i < parsed.Count && hits.Count < MaxShown; i++)
        {
            var item = parsed[i];
            var key = Trim(item.Key, MaxKeyLength);
            if (key is null) continue;

            // Anything but the one kind we recognise is filed as a page rather than written
            // through — the column is read back into SQL and rendered on an admin page.
            var kind = string.Equals(item.Kind, InstantSearchEventMapper.SectionKind, StringComparison.Ordinal)
                ? InstantSearchEventMapper.SectionKind
                : InstantSearchEventMapper.PageKind;

            hits.Add(new InstantSearchShownHit(
                Position: hits.Count + 1,
                Kind: kind,
                Key: key,
                Label: Trim(item.Label, MaxLabelLength) ?? string.Empty));
        }

        shown = hits;
        return true;
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class ShownDto
    {
        public string? Kind { get; set; }
        public string? Key { get; set; }
        public string? Label { get; set; }
    }
}

// The form a browser posts. Bound as a model rather than loose parameters so the field names
// live in one place alongside the JavaScript that fills them.
public sealed class InstantSearchReport
{
    public string? Surface { get; set; }
    public string? Q { get; set; }
    public string? Scope { get; set; }
    public string? HostPath { get; set; }

    // JSON array of {kind, key, label}, in the order the person saw them.
    public string? Shown { get; set; }

    public string? SelectedKey { get; set; }
    public int? SelectedPosition { get; set; }
    public int LatencyMs { get; set; }
}
