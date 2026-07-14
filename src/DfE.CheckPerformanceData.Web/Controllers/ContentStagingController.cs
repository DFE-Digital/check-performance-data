using System.Text;
using System.Text.Json;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Content staging: export/import wiki pages + content blocks as a schema-versioned JSON
// bundle between environments. Gated by the content-staging section grant.
[RequireAdminSection(AdminNavKeys.ContentStaging)]
[Route("admin/content-staging")]
public sealed class ContentStagingController(
    IContentStagingService staging,
    ICurrentUserService currentUser,
    IPageNodeRepository pageNodeRepository,
    ILogger<ContentStagingController> logger,
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    ISettingService settingService,
    IPortalDbContext? dbContext = null,
    IContentStagingLock? importLock = null) : Controller
{
    // Nullable + defaulted so the pre-existing unit tests (which construct the controller
    // with the required deps) keep compiling. Real DI wires the actual context in and the
    // audit-write path activates.
    private readonly IPortalDbContext? _dbContext = dbContext;
    private readonly IContentStagingLock? _importLock = importLock;

    // Clear-all wipes every page and content block in the environment: fine on a development or
    // throwaway environment, and it has no business being reachable on one holding content anyone
    // depends on.
    //
    // Available where the environment IS Development (local, and the deployed DEV app, which runs
    // under that environment name) or where dev tools are switched on (the ephemeral review apps
    // set Dev:ToolsEnabled) — and never in Production, whatever the configuration says.
    //
    // Deliberately NOT the plain Dev:ToolsEnabled test the /dev/* surfaces use. That flag is not
    // set on deployed DEV, so reusing it alone would take clear-all away from the very environment
    // it is wanted on; and setting the flag there to compensate would switch on dev impersonation
    // as a side effect, which DevImpersonationController states must never reach deployed DEV.
    private bool EnvironmentAllowsClearAll =>
        (hostEnvironment.IsDevelopment() || configuration.GetValue<bool>(SettingKeys.DevToolsEnabled))
        && !hostEnvironment.IsProduction();

    // Two gates, and both have to say yes.
    //
    // The environment one above is the boundary and is not editable: whatever an operator does on
    // the settings page, clear-all cannot appear on QA, preproduction or production. The setting
    // is the switch within the environments that may have it, defaulted off, so an environment
    // only offers it once somebody deliberately asks for it — and so the hidden state can be
    // exercised without redeploying with different configuration.
    private async Task<bool> ClearAllAvailableAsync() =>
        EnvironmentAllowsClearAll
        && await settingService.GetBoolAsync(SettingKeys.ShowDeleteAllButton);

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        // The view hides the whole reset section on this, so the button never appears where
        // the route would refuse it anyway.
        ViewData["ClearAllAvailable"] = await ClearAllAvailableAsync();
        return View();
    }

    // Whole-environment export (the "export everything" convenience button).
    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        var bundle = await staging.ExportAsync();
        return ExportFile(bundle);
    }

    // The selection page: choose which pages and blocks to export.
    [HttpGet("select")]
    public async Task<IActionResult> Select()
    {
        var catalog = await staging.GetCatalogAsync();
        return View(catalog);
    }

    // Export only the ticked pages/blocks (ancestors of selected pages are added by the service).
    [HttpPost("export")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportSelected(List<Guid>? pageNodeIds, List<Guid>? contentBlockIds)
    {
        var selection = new ContentExportSelection(
            pageNodeIds?.ToHashSet() ?? [],
            contentBlockIds?.ToHashSet() ?? []);

        if (selection.PageNodeIds.Count == 0 && selection.ContentBlockIds.Count == 0)
        {
            TempData["ContentStagingError"] = "Select at least one page or content block to export.";
            return Redirect("/admin/content-staging/select");
        }

        var bundle = await staging.ExportAsync(selection);
        return ExportFile(bundle);
    }

    private FileContentResult ExportFile(ContentBundle bundle)
    {
        var stamped = new ContentBundle
        {
            Schema = bundle.Schema,
            SchemaVersion = bundle.SchemaVersion,
            ExportedAtUtc = DateTime.UtcNow,
            ExportedBy = currentUser.Email,
            PageNodes = bundle.PageNodes,
            ContentBlocks = bundle.ContentBlocks
        };

        var bytes = Encoding.UTF8.GetBytes(ContentStagingJson.Serialize(stamped));
        var fileName = $"cpd-content-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
        return File(bytes, "application/json", fileName);
    }

    // Content-staging bundles from a real environment are commonly 5–20 MB (they carry
    // full version history and base64-embedded images). Raise the two ASP.NET default
    // request-size limits that would otherwise reject a legitimate export with an empty-
    // body 400 before the controller sees it:
    //  * Kestrel MaxRequestBodySize default ~28.6 MB — bumped to 64 MB for the file upload.
    //  * FormOptions ValueLengthLimit default ~4 MB per form value — bumped to 64 MB so the
    //    round-tripped BundleJson hidden field between Preview and Import gets through.
    // Attribute-scoped so the raised limits apply ONLY to these two endpoints, not the
    // whole app.
    private const int LargeBundleLimitBytes = 64 * 1024 * 1024;

    // Step 1 of import: read the uploaded file, analyse it against the current environment, and
    // show the preview so the administrator can see new vs colliding content and decide per item.
    // Cap the uploaded bundle at 50 MB. A typical whole-environment export tops out well
    // under 10 MB; anything larger is either a bug (accidentally exported a bunch of
    // base64-embedded images) or a DoS attempt via a maliciously-crafted JSON file. The cap
    // is applied against IFormFile.Length before we ever open the stream, so a hostile
    // upload can't force the JSON parser to allocate.
    private const long MaxBundleBytes = 50 * 1024 * 1024;

    [HttpPost("preview")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(LargeBundleLimitBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = LargeBundleLimitBytes)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Preview(IFormFile? bundle)
    {
        if (bundle is null || bundle.Length == 0)
        {
            TempData["ContentStagingError"] = "Choose a bundle file to import.";
            return Redirect("/admin/content-staging");
        }

        if (bundle.Length > MaxBundleBytes)
        {
            TempData["ContentStagingError"] =
                $"Bundle file is too large ({bundle.Length / (1024 * 1024)} MB). The limit is {MaxBundleBytes / (1024 * 1024)} MB.";
            return Redirect("/admin/content-staging");
        }

        string json;
        // Strict UTF-8: reject invalid byte sequences up front rather than silently
        // substituting U+FFFD replacement chars, which would corrupt content invisibly
        // and could round-trip through Import to land malformed strings in the DB.
        var strictUtf8 = new System.Text.UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            // detectEncodingFromByteOrderMarks:false — a leading 0xFF 0xFE would otherwise flip
            // StreamReader into UTF-16 LE mode, bypassing our strict UTF-8 encoding.
            using var reader = new StreamReader(
                bundle.OpenReadStream(), strictUtf8, detectEncodingFromByteOrderMarks: false);
            json = await reader.ReadToEndAsync();
        }
        catch (System.Text.DecoderFallbackException)
        {
            TempData["ContentStagingError"] =
                "Bundle file contains invalid UTF-8 bytes. Re-export from a supported environment.";
            return Redirect("/admin/content-staging");
        }

        if (!TryParseBundle(json, out var parsed, out var error))
        {
            TempData["ContentStagingError"] = error;
            return Redirect("/admin/content-staging");
        }

        ContentImportPreview preview;
        try
        {
            preview = await staging.PreviewAsync(parsed!);
        }
        catch (ContentImportValidationException ex)
        {
            TempData["ContentStagingError"] =
                "Bundle failed validation:\n" + string.Join("\n", ex.Issues.Select(i => "• " + i.Message));
            return Redirect("/admin/content-staging");
        }
        return View(new ImportPreviewViewModel
        {
            Preview = preview,
            BundleJson = ContentStagingJson.Serialize(parsed!)
        });
    }

    // Pasted-URL landing: /import is a POST-only step in the flow, but users sometimes visit the
    // URL directly from a bookmark or a link. Bounce them back to the landing page (which is
    // where the upload form actually lives) instead of letting the request fall through to the
    // catch-all PageController and 404/500 depending on environment.
    [HttpGet("import")]
    public IActionResult ImportLanding() => Redirect("/admin/content-staging");

    // Destructive: truncates every PageNode / PageNodeVersion / ContentBlock / ContentBlockVersion
    // row. Used to reset a test environment to empty before replaying an import bundle. Gated by
    // the same editor role as the rest of the controller and behind a confirm modal on the view.
    // Default startup seeders (root nodes, /help/not-found) will re-run and rehydrate a minimal
    // shell on the next request.
    [HttpPost("clear-all")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearAll()
    {
        // Refuse on the route, not just in the view. Hiding the button would still leave a
        // destructive POST reachable by anyone with the content-staging grant who knows the URL.
        if (!await ClearAllAvailableAsync())
        {
            logger.LogWarning(
                "ContentStaging: clear-all refused (environment={Environment}, environmentAllows={EnvironmentAllows})",
                hostEnvironment.EnvironmentName, EnvironmentAllowsClearAll);
            return NotFound();
        }

        await pageNodeRepository.TruncateAllContentAsync();
        TempData["ContentStagingResult"] =
            "All CMS pages and content blocks were cleared. Default root nodes will regenerate on the next request.";
        return Redirect("/admin/content-staging");
    }

    // Step 2 of import: apply the previewed bundle with the chosen global mode and any per-item
    // overrides. The bundle round-trips through a hidden field so no server-side state is needed —
    // that field is BundleJson and can easily exceed the 4 MB default ValueLengthLimit for a real
    // environment export, so the form-limits attribute below has to raise the ceiling or the
    // model binder rejects the request with an empty-body 400 before this action runs.
    [HttpPost("import")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(LargeBundleLimitBytes)]
    [RequestFormLimits(ValueLengthLimit = LargeBundleLimitBytes)]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Import(ImportConfirmFormModel model)
    {
        ContentBundle? parsed = null;
        string? error = null;
        if (string.IsNullOrEmpty(model.BundleJson) || !TryParseBundle(model.BundleJson, out parsed, out error))
        {
            TempData["ContentStagingError"] = error ?? "The import session expired. Upload the file again.";
            return Redirect("/admin/content-staging");
        }

        var decisions = model.Decisions
            .Where(d => d.Action.HasValue)
            .GroupBy(d => d.Id)
            .ToDictionary(g => g.Key, g => g.First().Action!.Value);

        logger.LogInformation(
            "Import controller: bundle={PageCount} pages / {BlockCount} blocks, collisionMode={Mode}, newItemMode={NewMode}, perItemDecisions={DecisionCount}",
            parsed!.PageNodes.Count, parsed.ContentBlocks.Count, model.GlobalMode, model.GlobalNewMode, decisions.Count);

        // Cross-pod concurrency guard. Two admins hitting Import at the same second — one
        // on each of two pods — would race on individual pages and produce a chaotic mixed
        // outcome. Acquire a Postgres advisory lock first; if a peer holds it, surface a
        // specific "another import is in progress" banner rather than let them collide.
        var lockAcquired = _importLock is not null && await _importLock.TryAcquireAsync();
        if (_importLock is not null && !lockAcquired)
        {
            TempData["ContentStagingError"] =
                "Another import is already in progress. Wait for it to finish and try again.";
            return Redirect("/admin/content-staging");
        }

        try
        {
            var result = await staging.ImportAsync(parsed!, model.GlobalMode, decisions, model.GlobalNewMode);
            TempData["ContentStagingResult"] = BuildSummary(result);
            if (result.Errors.Count > 0)
                TempData["ContentStagingError"] = string.Join("\n", result.Errors);
            if (result.Warnings.Count > 0)
                TempData["ContentStagingWarnings"] = string.Join("\n", result.Warnings);

            // Forensic trail — every bulk destructive admin action lands in AuditEntries so
            // an incident-responder can reconstruct "who imported what, when" without
            // grepping app logs. Only successful imports are recorded (failed imports have
            // no net effect worth tracing). Matches QueueAdminController's audit pattern.
            await WriteImportAuditAsync(result, model.BundleJson?.Length ?? 0);
        }
        catch (ContentImportConflictException ex)
        {
            TempData["ContentStagingError"] = ex.Message;
        }
        catch (ContentImportValidationException ex)
        {
            // Re-validation on Import trips if the round-tripped bundle JSON has been tampered
            // with. Surface the specific issues rather than a generic message so the operator
            // can see exactly what's wrong with the payload.
            TempData["ContentStagingError"] =
                "Bundle failed validation on import:\n" + string.Join("\n", ex.Issues.Select(i => "• " + i.Message));
        }
        catch (Exception ex)
        {
            // The service handles per-item failures internally, so this catches only the
            // catastrophic-outer-flow case — e.g. a database connection failure or a JSON edge case
            // that the parser missed. Log the full exception so the review-app logs surface it,
            // and hand the user a coherent error instead of a blank 500 page.
            logger.LogError(ex, "Import controller: bundle import failed with an unhandled exception");
            TempData["ContentStagingError"] =
                $"Import failed: {ex.GetType().Name} — {ex.Message}. Check the application logs for the full stack trace.";
        }
        finally
        {
            // Always release — even if the import threw, we must free the lock for the
            // next caller. Advisory-lock release is scoped to the session we acquired on,
            // so a peer's separate session is unaffected either way.
            if (lockAcquired && _importLock is not null)
            {
                await _importLock.ReleaseAsync();
            }
        }

        return Redirect("/admin/content-staging");
    }

    private static bool TryParseBundle(string json, out ContentBundle? bundle, out string? error)
    {
        try
        {
            bundle = ContentStagingJson.Deserialize(json);
        }
        catch (JsonException)
        {
            bundle = null;
        }

        if (bundle is null || bundle.Schema != ContentBundle.CurrentSchema)
        {
            error = $"The file is not a valid '{ContentBundle.CurrentSchema}' content bundle.";
            return false;
        }

        if (bundle.SchemaVersion != ContentBundle.CurrentSchemaVersion)
        {
            error = $"This bundle is schema version {bundle.SchemaVersion}, but this environment only " +
                    $"supports version {ContentBundle.CurrentSchemaVersion}.";
            return false;
        }

        error = null;
        return true;
    }

    private static string BuildSummary(ContentImportResult r) =>
        $"Import complete. Pages: {r.PageNodesCreated} added, {r.PageNodesUpdated} updated, " +
        $"{r.PageNodesSkipped} skipped. Content blocks: {r.ContentBlocksCreated} added, " +
        $"{r.ContentBlocksUpdated} updated, {r.ContentBlocksSkipped} skipped.";

    // Records an AuditEntry for a successful content-staging import. NewValues carries a
    // JSON summary of the mutation counts so an incident-responder can reconstruct "who
    // changed how much" without replaying the bundle. Guarded on _dbContext so tests that
    // construct the controller without a context (all pre-existing tests) don't NRE.
    private async Task WriteImportAuditAsync(ContentImportResult result, int bundleJsonBytes)
    {
        if (_dbContext is null) return;

        var summary = JsonSerializer.Serialize(new
        {
            result.PageNodesCreated,
            result.PageNodesUpdated,
            result.PageNodesSkipped,
            result.ContentBlocksCreated,
            result.ContentBlocksUpdated,
            result.ContentBlocksSkipped,
            WarningCount = result.Warnings.Count,
            ErrorCount = result.Errors.Count,
            BundleJsonBytes = bundleJsonBytes,
        });

        _dbContext.AuditEntries.Add(new AuditEntry
        {
            EntityType = "ContentBundle",
            EntityId = "import",
            Action = "Import",
            NewValues = summary,
            Timestamp = DateTime.UtcNow,
            UserId = currentUser?.UserId,
        });
        await _dbContext.SaveChangesAsync();
    }
}
