using System.Globalization;
using System.Text.Json;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Sample-data admin surface for the seed-sample-search-data page. Gated by two
// independent controls:
//   * [RequireAdminSection(TestDataGroup)] — a fresh-DB admin has this grant through
//     DefaultAdminAccessSeeder.AllSections; editor-only users 404 at the attribute.
//   * IHostEnvironment.IsSampleDataAdminEnvironment() — explicit whitelist of
//     Development + Review (per-PR review app). QA, Preproduction and Production
//     return 404 verbatim even for a principal that carries the section grant. The
//     endpoint is destructive-adjacent (the Danger zone can DELETE ALL rows in the
//     sink including live traffic) so the env guard is defence-in-depth alongside
//     the section-access check.
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

    // Literal the "delete all data" typed-confirmation guard checks. Kept as a compile-
    // time const so a code-search shows every place the invariant is enforced (form
    // field, query-string branch, JS enablement rule) and cannot drift.
    internal const string DeleteAllConfirmationLiteral = "DELETE";

    private readonly SampleSearchDataSeeder _seeder;
    private readonly IHostEnvironment _environment;
    private readonly IPortalDbContext? _dbContext;
    private readonly ICurrentUserService? _currentUserService;
    private readonly ISampleSearchDataSeedJobStore _jobStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISampleSearchDataGateway? _adminGateway;
    private readonly ISettingService? _settings;

    public TestDataController(
        SampleSearchDataSeeder seeder,
        IHostEnvironment environment,
        ISampleSearchDataSeedJobStore jobStore,
        IServiceScopeFactory scopeFactory,
        IPortalDbContext? dbContext = null,
        ICurrentUserService? currentUserService = null,
        ISampleSearchDataGateway? adminGateway = null,
        ISettingService? settings = null)
    {
        _seeder = seeder;
        _environment = environment;
        _jobStore = jobStore;
        _scopeFactory = scopeFactory;
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _adminGateway = adminGateway;
        _settings = settings;
    }

    // GET /admin/test-data/sample-search-data — renders the preset form. When called with
    // ?jobId=... the view auto-opens the progress modal and lets the JS poller pick up
    // from the current job snapshot (page reload mid-seed / JS-less redirect landing).
    // Also carries the persisted per-event seconds rate so the JS can render an initial
    // ETA on modal open before any real ticks arrive.
    [HttpGet("sample-search-data")]
    public async Task<IActionResult> SampleSearchData(Guid? jobId = null)
    {
        if (!_environment.IsSampleDataAdminEnvironment())
        {
            return NotFound();
        }

        ViewData["AdminActiveKey"] = AdminNavKeys.SeedSampleSearchData;
        ViewData["Title"] = "Seed sample search data";

        var banner = TempData[TempDataKey] as string;

        var secondsPerEvent = _settings is not null
            ? await _settings.GetDoubleAsync(SettingKeys.SearchAnalyticsSeedSecondsPerEvent)
            : 0.1;

        return View("~/Views/Admin/TestData/SampleSearchData.cshtml",
            new SeedSampleSearchDataViewModel
            {
                SuccessBanner = banner,
                ActiveJobId = jobId,
                SecondsPerEvent = secondsPerEvent,
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
        if (!_environment.IsSampleDataAdminEnvironment())
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
        if (!_environment.IsSampleDataAdminEnvironment())
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
    // (via fetch). Semantically this is "interrupt AND roll back": whether the seeder is
    // still running or finished before the click landed, Cancel drops every row THIS run
    // wrote via the per-job-id marker. Flow:
    //   1. RequestCancel — signals the CTS (no-op if the job already ran to completion).
    //   2. Poll the store briefly (up to ~3 s) for the seeder to unwind to a terminal
    //      state so we do not race with an in-flight batch flush.
    //   3. Invoke gateway.DeleteByJobIdAsync — one transaction across the three sink
    //      tables.
    //   4. MarkCompleted with a "Cancelled and rolled back N rows" note.
    // Returns 200 OK with a JSON body { rolledBackRows, note } that the JS surfaces in
    // the modal's cancelled state.
    [HttpDelete("sample-search-data/{jobId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelSampleSearchDataSeed(
        Guid jobId, CancellationToken cancellationToken)
    {
        if (!_environment.IsSampleDataAdminEnvironment())
        {
            return NotFound();
        }

        // Cheap 404 for a genuinely unknown job (e.g. evicted from the retention window).
        var initialSnapshot = _jobStore.Get(jobId);
        if (initialSnapshot is null)
        {
            return NotFound();
        }

        // Signal the token if still running. The store now returns true for any known
        // job regardless of state (Completed jobs are legitimate rollback targets).
        _ = _jobStore.RequestCancel(jobId);

        // Wait briefly for the seeder to unwind. In practice: the seeder respects the
        // token between batches (typically < 500 ms) or has already completed. Bounded
        // so a wedged seeder cannot block the admin's rollback indefinitely.
        var settleDeadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < settleDeadline)
        {
            var snap = _jobStore.Get(jobId);
            if (snap is null
                || snap.State == SampleSearchDataSeedJobState.Completed
                || snap.State == SampleSearchDataSeedJobState.Failed)
            {
                break;
            }
            await Task.Delay(50, cancellationToken);
        }

        DeleteCountsResult? rollback = null;
        var startedAtUtc = DateTime.UtcNow;
        if (_adminGateway is not null)
        {
            rollback = await _adminGateway.DeleteByJobIdAsync(jobId.ToString("N"),
                cancellationToken);
            await WriteDeleteAuditAsync(
                action: "SampleSearchDataSeedRolledBack",
                counts: rollback,
                startedAtUtc: startedAtUtc,
                cancellationToken: cancellationToken);
        }

        var totalRolledBack = (rollback?.EventsDeleted ?? 0)
                              + (rollback?.MessagesDeleted ?? 0);
        var note = rollback is not null
            ? $"Cancelled and rolled back {rollback.EventsDeleted:N0} events " +
              $"and {rollback.MessagesDeleted:N0} messages."
            : "Cancelled.";

        // Terminal transition — replace whatever intermediate state the poll loop left.
        // Use int.MinValue-safe accessor: audit id may be zero if the earlier audit
        // insert failed; the store handles that.
        _jobStore.MarkCompleted(jobId, auditEntryId: 0, note: note);

        return Ok(new
        {
            rolledBackRows = totalRolledBack,
            eventsRolledBack = rollback?.EventsDeleted ?? 0,
            messagesRolledBack = rollback?.MessagesDeleted ?? 0,
            note,
        });
    }

    // POST /admin/test-data/sample-search-data/delete-seeded — drops every row with
    // is_seeded = true across search_events, search_event_results and search_messages
    // in one transaction. Real user activity (is_seeded = false) is preserved so a
    // support team member can still see genuine feedback after the demo data is wiped.
    // Anti-forgery-protected and dev-only.
    [HttpPost("sample-search-data/delete-seeded")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSeededSampleSearchData(
        CancellationToken cancellationToken)
    {
        if (!_environment.IsSampleDataAdminEnvironment())
        {
            return NotFound();
        }
        if (_adminGateway is null)
        {
            return StatusCode(500);
        }

        var startedAtUtc = DateTime.UtcNow;
        var result = await _adminGateway.DeleteSeededAsync(cancellationToken);
        await WriteDeleteAuditAsync(
            action: "DeleteSeededSearchData",
            counts: result,
            startedAtUtc: startedAtUtc,
            cancellationToken: cancellationToken);

        TempData[TempDataKey] = FormatDeleteBanner(result);
        return RedirectToAction(nameof(SampleSearchData));
    }

    // POST /admin/test-data/sample-search-data/delete-all — TRUNCATE-equivalent across
    // the same three tables regardless of the seeded marker. Requires an additional
    // typed-confirmation guard (?confirm=DELETE or a `confirm` form field) — the
    // endpoint 400s without it so a rogue automation cannot drop the whole sink with
    // one anti-forgery token. Anti-forgery-protected and dev-only.
    [HttpPost("sample-search-data/delete-all")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAllSampleSearchData(
        [FromForm(Name = "confirm")] string? confirmForm,
        [FromQuery(Name = "confirm")] string? confirmQuery,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsSampleDataAdminEnvironment())
        {
            return NotFound();
        }
        if (_adminGateway is null)
        {
            return StatusCode(500);
        }

        var supplied = confirmForm ?? confirmQuery;
        if (!string.Equals(supplied, DeleteAllConfirmationLiteral, StringComparison.Ordinal))
        {
            return BadRequest(
                $"Typed confirmation required — expected exactly '{DeleteAllConfirmationLiteral}'.");
        }

        var startedAtUtc = DateTime.UtcNow;
        var result = await _adminGateway.DeleteAllAsync(cancellationToken);
        await WriteDeleteAuditAsync(
            action: "DeleteAllSearchData",
            counts: result,
            startedAtUtc: startedAtUtc,
            cancellationToken: cancellationToken);

        TempData[TempDataKey] = FormatDeleteBanner(result);
        return RedirectToAction(nameof(SampleSearchData));
    }

    private static string FormatDeleteBanner(DeleteCountsResult r) =>
        $"Deleted {r.EventsDeleted:N0} events, {r.ResultsDeleted:N0} results, {r.MessagesDeleted:N0} messages from the sink.";

    private async Task WriteDeleteAuditAsync(
        string action,
        DeleteCountsResult counts,
        DateTime startedAtUtc,
        CancellationToken cancellationToken)
    {
        if (_dbContext is null)
        {
            return;
        }

        var completedAtUtc = DateTime.UtcNow;
        var actingUserId = _currentUserService?.UserId;
        var payload = JsonSerializer.Serialize(new
        {
            eventsDeleted = counts.EventsDeleted,
            resultsDeleted = counts.ResultsDeleted,
            messagesDeleted = counts.MessagesDeleted,
            deletedBy = actingUserId,
            deletedAt = completedAtUtc,
            durationMs = (int)(completedAtUtc - startedAtUtc).TotalMilliseconds,
        });

        _dbContext.AuditEntries.Add(new AuditEntry
        {
            EntityType = "SearchAnalyticsSink",
            EntityId = "sample-search-data",
            Action = action,
            NewValues = payload,
            Timestamp = completedAtUtc,
            UserId = actingUserId,
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
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
        // Wall-clock timing for the EMA update: measured against actual seed duration
        // rather than the seeder's per-batch cadence so it reflects real throughput
        // (batch flush latency + progress-tick overhead + everything else).
        var startedAtUtc = DateTime.UtcNow;

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
                progress,
                jobId: jobId);
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        // Deliberately no `finally { cts.Dispose(); }` here — disposal moved to AFTER
        // MarkCompleted / MarkFailed below. Otherwise a Cancel click that lands in the
        // race window between "seeder returns" and "job store learns the terminal state"
        // catches ObjectDisposedException on the CTS and cannot signal the intent. The
        // store's Running/Cancelling guards handle idempotency correctly.

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
                startedAtUtc,
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

                // Adaptive EMA of the per-event seconds rate. Only fires on non-cancelled
                // non-failed completions — a cancelled or failed run doesn't reflect true
                // throughput. Isolated in its own try/catch so a settings-store hiccup
                // never derails the job state transition above.
                try
                {
                    var settings = scope.ServiceProvider.GetService<ISettingService>();
                    if (settings is not null
                        && result is not null
                        && result.EventsCreated > 0)
                    {
                        var elapsedSeconds = (DateTime.UtcNow - startedAtUtc).TotalSeconds;
                        var measured = elapsedSeconds / result.EventsCreated;
                        var stored = await settings.GetDoubleAsync(
                            SettingKeys.SearchAnalyticsSeedSecondsPerEvent);
                        var blended = SeedRateEma.Blend(stored, measured);
                        await settings.SaveAsync(
                            SettingKeys.SearchAnalyticsSeedSecondsPerEvent,
                            blended.ToString("0.######", CultureInfo.InvariantCulture));
                    }
                }
                catch
                {
                    // Non-fatal. The next seed will just start from the previous stored
                    // value and re-attempt the blend.
                }
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
        finally
        {
            // Safe to dispose now — state transition has already fired, and any Cancel
            // click that landed on a disposed CTS was tolerated by RequestCancel.
            try { cts.Dispose(); } catch { /* already disposed by another race */ }
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
            // Correlate the audit row with any elevated-latency bar the reviewer sees
            // on the dashboard for the same time range. Empty when the variance layer
            // did not insert an outage for this run (short presets).
            outageWindowsUtc = result?.OutageWindowsUtc?
                .Select(o => new[] { o.StartUtc, o.EndUtc })
                .ToArray(),
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
