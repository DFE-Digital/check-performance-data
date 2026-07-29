using System.Text.Json;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Dev-only surface for the seed-sample-search-data admin page. Gated by two independent
// controls:
//   * [RequireAdminSection(TestDataGroup)] — a fresh-DB admin has this grant through
//     DefaultAdminAccessSeeder.AllSections; editor-only users 404 at the attribute.
//   * IHostEnvironment.IsDevelopment() — production returns 404 verbatim even for a
//     principal that carries the grant. The endpoint is destructive-adjacent so the
//     env guard is defence-in-depth alongside the section-access check.
//
// Seed runs are non-blocking: the POST creates a job in the store, kicks the seeder onto
// a background Task.Run with its own scope, and redirects to the same page with a jobId
// query-string parameter. The client-side JS opens a modal and polls the /progress
// endpoint every 500 ms so the user sees rows-written + current-cursor updates in real
// time. Cancellation is wired through the DELETE endpoint.
[RequireAdminSection(AdminNavKeys.TestDataGroup)]
[Route("admin/test-data")]
public sealed class TestDataController : Controller
{
    private const string TempDataKey = "SeedSampleSearchDataResult";

    private readonly SampleSearchDataSeeder _seeder;
    private readonly IHostEnvironment _environment;
    private readonly IPortalDbContext? _dbContext;
    private readonly ICurrentUserService? _currentUserService;
    private readonly ISampleSearchDataSeedJobStore _jobStore;
    private readonly IServiceScopeFactory _scopeFactory;

    public TestDataController(
        SampleSearchDataSeeder seeder,
        IHostEnvironment environment,
        ISampleSearchDataSeedJobStore jobStore,
        IServiceScopeFactory scopeFactory,
        IPortalDbContext? dbContext = null,
        ICurrentUserService? currentUserService = null)
    {
        _seeder = seeder;
        _environment = environment;
        _jobStore = jobStore;
        _scopeFactory = scopeFactory;
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    // GET /admin/test-data/sample-search-data — renders the preset form. When called with
    // ?jobId=... the view auto-opens the progress modal and lets the JS poller pick up
    // from the current job snapshot (page reload mid-seed / JS-less redirect landing).
    [HttpGet("sample-search-data")]
    public IActionResult SampleSearchData(Guid? jobId = null)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        ViewData["AdminActiveKey"] = AdminNavKeys.SeedSampleSearchData;
        ViewData["Title"] = "Seed sample search data";

        var banner = TempData[TempDataKey] as string;
        return View("~/Views/Admin/TestData/SampleSearchData.cshtml",
            new SeedSampleSearchDataViewModel
            {
                SuccessBanner = banner,
                ActiveJobId = jobId,
            });
    }

    // POST /admin/test-data/sample-search-data — non-blocking. Creates a job in the store,
    // kicks the seeder on a Task.Run with its own scope, redirects to GET with the JobId.
    // The client-side JS intercepts the submit (fetch-then-modal) so the browser never
    // sees the redirect; the redirect exists to make the page work with JS disabled.
    [HttpPost("sample-search-data")]
    [ValidateAntiForgeryToken]
    public IActionResult SeedSampleSearchData(
        string preset,
        int? eventCount,
        int? messageCount)
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
        // no session id is available.
        var sessionId = HttpContext?.Session?.Id ?? Guid.NewGuid().ToString();
        var rngSeed = sessionId.GetHashCode() ^ nowUtc.Ticks.GetHashCode();
        var actingUserId = _currentUserService?.UserId;

        var cts = new CancellationTokenSource();
        var job = _jobStore.Register(presetLabel, resolvedEventCount, resolvedMessageCount, cts);

        // Fire the seed on a background task with its own scope — the request scope is torn
        // down as soon as the redirect returns, so the DbContext + gateway captured by the
        // request-scoped seeder cannot be reused. Task.Run avoids blocking the HTTP thread
        // pool with the potentially minute-scale seed.
        _ = Task.Run(() => RunSeedAsync(
            job.JobId,
            span,
            resolvedEventCount,
            resolvedMessageCount,
            nowUtc,
            rngSeed,
            preset,
            presetLabel,
            actingUserId,
            cts));

        return RedirectToAction(nameof(SampleSearchData), new { jobId = job.JobId });
    }

    // GET /admin/test-data/sample-search-data/progress?jobId=… — small JSON payload used by
    // the modal poller. Returns 404 when the id is unknown (either evicted or a bad
    // client). Dev-only + access-controlled same as the main actions.
    [HttpGet("sample-search-data/progress")]
    public IActionResult SampleSearchDataProgress(Guid jobId)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var job = _jobStore.Get(jobId);
        if (job is null)
        {
            return NotFound();
        }

        return Json(new
        {
            jobId = job.JobId,
            state = job.State.ToString(),
            eventsWritten = job.EventsWritten,
            eventsTotal = job.EventsTotal,
            messagesWritten = job.MessagesWritten,
            messagesTotal = job.MessagesTotal,
            currentCursorUtc = job.CurrentCursorUtc,
            startedAtUtc = job.StartedAtUtc,
            completedAtUtc = job.CompletedAtUtc,
            errorMessage = job.ErrorMessage,
            presetLabel = job.PresetLabel,
            auditEntryId = job.AuditEntryId == 0 ? (int?)null : job.AuditEntryId,
            note = job.Note,
        });
    }

    // DELETE /admin/test-data/sample-search-data/{jobId} — modal Cancel button posts here
    // (via fetch). Signals the job's CancellationTokenSource; the seeder catches
    // OperationCanceledException between batches and the background task calls MarkCompleted
    // with a "cancelled at N events" note.
    [HttpDelete("sample-search-data/{jobId:guid}")]
    [ValidateAntiForgeryToken]
    public IActionResult CancelSampleSearchDataSeed(Guid jobId)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var cancelled = _jobStore.RequestCancel(jobId);
        return cancelled ? NoContent() : NotFound();
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

    // Background seed runner. Owns its own scope + resolves a fresh seeder / dbcontext so
    // the seed lives past the originating HTTP request. Wraps the seeder in progress-tick
    // wiring so each batch flush updates the job store; catches OperationCanceledException
    // (turns into "Completed with cancelled note") and generic Exception (turns into
    // "Failed with error message"). Writes the AuditEntry at the end regardless of outcome
    // so the forensic trail records every seed attempt.
    private async Task RunSeedAsync(
        Guid jobId,
        TimeSpan span,
        int eventCount,
        int messageCount,
        DateTime nowUtc,
        int rngSeed,
        string preset,
        string presetLabel,
        string? actingUserId,
        CancellationTokenSource cts)
    {
        SampleSearchDataSeedResult? result = null;
        Exception? failure = null;
        var wasCancelled = false;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var seeder = scope.ServiceProvider.GetRequiredService<SampleSearchDataSeeder>();

            var progress = new Progress<SampleSearchDataSeedProgressTick>(tick =>
                _jobStore.UpdateProgress(jobId, tick));

            result = await seeder.SeedAsync(
                span,
                eventCount,
                messageCount,
                nowUtc,
                rngSeed,
                cts.Token,
                progress);
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            cts.Dispose();
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetService<IPortalDbContext>();
            var auditIdLong = await WriteAuditAsync(
                dbContext,
                jobId,
                preset,
                presetLabel,
                result,
                eventCount,
                messageCount,
                nowUtc,
                actingUserId,
                wasCancelled,
                failure);

            // AuditEntry.Id is a long. The client-facing snapshot exposes an int since the
            // audit table primary key is far below 2^31 in this deployment.
            var auditId = (int)Math.Min(int.MaxValue, auditIdLong);

            if (failure is not null)
            {
                _jobStore.MarkFailed(jobId, failure.Message);
            }
            else if (wasCancelled)
            {
                var snapshot = _jobStore.Get(jobId);
                var note = snapshot is not null
                    ? $"Cancelled by admin at {snapshot.EventsWritten:N0} events."
                    : "Cancelled by admin.";
                _jobStore.MarkCompleted(jobId, auditId, note);
            }
            else
            {
                _jobStore.MarkCompleted(jobId, auditId);
            }
        }
        catch
        {
            // Audit write failed AFTER the seed itself. Do NOT overwrite a genuine seed
            // failure; still transition Running -> Completed so the modal can close. The
            // caller has the seeded rows regardless.
            if (failure is null && !wasCancelled)
            {
                _jobStore.MarkCompleted(jobId, 0);
            }
        }
    }

    // Records the seed run to the AuditEntries table so a forensic trail exists — who
    // seeded what and when. Adds the JobId and duration so a reviewer can correlate an
    // audit row with the in-memory job that produced it. Returns the AuditEntry primary
    // key so the store can expose it as a "view audit" link from the modal completed state.
    private async Task<long> WriteAuditAsync(
        IPortalDbContext? dbContext,
        Guid jobId,
        string preset,
        string presetLabel,
        SampleSearchDataSeedResult? result,
        int resolvedEventCount,
        int resolvedMessageCount,
        DateTime startedAtUtc,
        string? actingUserId,
        bool wasCancelled,
        Exception? failure)
    {
        if (dbContext is null)
        {
            return 0;
        }

        var completedAtUtc = DateTime.UtcNow;
        var durationSeconds = (int)Math.Max(0, (completedAtUtc - startedAtUtc).TotalSeconds);
        var action = wasCancelled
            ? "SampleSearchDataSeedCancelled"
            : failure is not null
                ? "SampleSearchDataSeedFailed"
                : "SeedSampleSearchData";

        var payload = JsonSerializer.Serialize(new
        {
            jobId,
            preset,
            presetLabel,
            requestedEvents = resolvedEventCount,
            requestedMessages = resolvedMessageCount,
            eventsCreated = result?.EventsCreated ?? 0,
            resultsCreated = result?.ResultsCreated ?? 0,
            messagesCreated = result?.MessagesCreated ?? 0,
            durationSeconds,
            seededBy = actingUserId,
            seededAt = completedAtUtc,
            error = failure?.Message,
        });

        var entry = new AuditEntry
        {
            EntityType = "SearchAnalyticsSink",
            EntityId = jobId.ToString(),
            Action = action,
            NewValues = payload,
            Timestamp = completedAtUtc,
            UserId = actingUserId,
        };
        dbContext.AuditEntries.Add(entry);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return entry.Id;
    }
}
