using System.Text.Json;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Dev-only surface for the seed-sample-search-data admin page. Gated by two independent
// controls:
//   * [RequireAdminSection(TestDataGroup)] — a fresh-DB admin has this grant through
//     DefaultAdminAccessSeeder.AllSections; editor-only users 404 at the attribute.
//   * IHostEnvironment.IsDevelopment() — production returns 404 verbatim even for a
//     principal that carries the grant. The endpoint is destructive-adjacent so the
//     env guard is defence-in-depth alongside the section-access check.
[RequireAdminSection(AdminNavKeys.TestDataGroup)]
[Route("admin/test-data")]
public sealed class TestDataController : Controller
{
    private const string TempDataKey = "SeedSampleSearchDataResult";

    private readonly SampleSearchDataSeeder _seeder;
    private readonly IHostEnvironment _environment;
    private readonly IPortalDbContext? _dbContext;
    private readonly ICurrentUserService? _currentUserService;

    public TestDataController(
        SampleSearchDataSeeder seeder,
        IHostEnvironment environment,
        IPortalDbContext? dbContext = null,
        ICurrentUserService? currentUserService = null)
    {
        _seeder = seeder;
        _environment = environment;
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    // GET /admin/test-data/sample-search-data — renders the preset form + optional success
    // banner. TempData carries the banner text across the POST-redirect handoff.
    [HttpGet("sample-search-data")]
    public IActionResult SampleSearchData()
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        ViewData["AdminActiveKey"] = AdminNavKeys.SeedSampleSearchData;
        ViewData["Title"] = "Seed sample search data";

        var banner = TempData[TempDataKey] as string;
        return View("~/Views/Admin/TestData/SampleSearchData.cshtml",
            new SeedSampleSearchDataViewModel { SuccessBanner = banner });
    }

    // POST /admin/test-data/sample-search-data — runs the seeder with the resolved preset
    // + optional overrides. Writes an AuditEntry describing the run before redirecting
    // back to the form with the count in TempData. The seeder handles its own transaction
    // per batch; a mid-run failure leaves the already-committed batches in place (that is
    // fine because the endpoint is additive and the caller can click again).
    [HttpPost("sample-search-data")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SeedSampleSearchData(
        string preset,
        int? eventCount,
        int? messageCount,
        CancellationToken ct = default)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var (span, defaultEvents, presetLabel) = ResolvePreset(preset);
        var resolvedEventCount = ClampCount(eventCount ?? defaultEvents, min: 1, max: 200_000);
        var defaultMessages = Math.Max(1, resolvedEventCount / 12); // ~8% of events
        var resolvedMessageCount = ClampCount(messageCount ?? defaultMessages, min: 0, max: 20_000);

        var nowUtc = DateTime.UtcNow;
        // Seed derived from the current session so repeat clicks accumulate variety rather
        // than reproducing the same pseudo-random draw. Falls back to time-based seed when
        // no session id is available (bare unit-scope construction, no HttpContext session).
        var sessionId = HttpContext?.Session?.Id ?? Guid.NewGuid().ToString();
        var rngSeed = sessionId.GetHashCode() ^ nowUtc.Ticks.GetHashCode();

        var result = await _seeder.SeedAsync(
            span, resolvedEventCount, resolvedMessageCount, nowUtc, rngSeed, ct);

        await WriteAuditAsync(preset, presetLabel, result, resolvedEventCount, resolvedMessageCount, ct);

        TempData[TempDataKey] =
            $"Seeded {result.EventsCreated:N0} search events, {result.ResultsCreated:N0} result rows " +
            $"and {result.MessagesCreated:N0} feedback messages spanning {presetLabel}. " +
            $"Visit /admin/Search/ to see them.";

        return RedirectToAction(nameof(SampleSearchData));
    }

    // Preset lookup: T4-defined defaults. Absent / unknown preset snaps to "Last 24 hours"
    // so a hand-edited POST body still produces a valid seed run rather than 400ing.
    internal static (TimeSpan Span, int DefaultEvents, string Label) ResolvePreset(string? preset)
    {
        return preset switch
        {
            "week"    => (TimeSpan.FromDays(7),    2_000,  "the last week"),
            "month"   => (TimeSpan.FromDays(30),   8_000,  "the last month"),
            "quarter" => (TimeSpan.FromDays(90),   25_000, "the last quarter"),
            "year"    => (TimeSpan.FromDays(365),  80_000, "the last year"),
            _         => (TimeSpan.FromHours(24),  500,    "the last 24 hours"),
        };
    }

    private static int ClampCount(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    // Records the seed run to the AuditEntries table so a forensic trail exists — who
    // seeded what and when. Skipped when the persistence context isn't wired (bare unit
    // construction). Uses "SeedSampleSearchData" as the Action value and serialises the
    // resolved parameters + counts into NewValues.
    private async Task WriteAuditAsync(
        string preset,
        string presetLabel,
        SampleSearchDataSeedResult result,
        int resolvedEventCount,
        int resolvedMessageCount,
        CancellationToken cancellationToken)
    {
        if (_dbContext is null)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            preset,
            presetLabel,
            requestedEvents = resolvedEventCount,
            requestedMessages = resolvedMessageCount,
            eventsCreated = result.EventsCreated,
            resultsCreated = result.ResultsCreated,
            messagesCreated = result.MessagesCreated,
            seededBy = _currentUserService?.UserId,
            seededAt = DateTime.UtcNow,
        });

        _dbContext.AuditEntries.Add(new AuditEntry
        {
            EntityType = "SearchAnalyticsSink",
            EntityId = "sample-seed",
            Action = "SeedSampleSearchData",
            NewValues = payload,
            Timestamp = DateTime.UtcNow,
            UserId = _currentUserService?.UserId,
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
