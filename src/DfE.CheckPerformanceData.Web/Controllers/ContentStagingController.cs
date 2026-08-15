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
    IContentStagingSessionStore sessions,
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

    // Whole-environment export (the "export everything" convenience button). Null id sets ask
    // for everything; only the history preference is carried.
    [HttpGet("export")]
    public async Task<IActionResult> Export(bool fullHistory = false)
    {
        var bundle = await staging.ExportAsync(
            new ContentExportSelection(MaxVersionsPerNode: MaxVersionsFor(fullHistory)));
        return ExportFile(bundle);
    }

    // "Include full version history" on either export form. Off means the default per-page cap;
    // on lifts it entirely, which is what a cross-environment migration needs.
    private static int? MaxVersionsFor(bool fullHistory) =>
        fullHistory ? null : ContentExportSelection.DefaultMaxVersionsPerNode;

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
    public async Task<IActionResult> ExportSelected(
        List<Guid>? pageNodeIds, List<Guid>? contentBlockIds, bool fullHistory = false)
    {
        var selection = new ContentExportSelection(
            pageNodeIds?.ToHashSet() ?? [],
            contentBlockIds?.ToHashSet() ?? [],
            MaxVersionsFor(fullHistory));

        if (selection.PageNodeIds!.Count == 0 && selection.ContentBlockIds!.Count == 0)
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

        // Zipped on the wire: bundles are repetitive JSON, so this is roughly an order of
        // magnitude off the download and the subsequent re-upload into the target environment.
        // Import sniffs the content, so a bundle exported before this still imports and a
        // hand-unzipped one does too.
        var bytes = ContentBundleArchive.ToZip(ContentStagingJson.Serialize(stamped));
        var fileName = $"cpd-content-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";
        return File(bytes, "application/zip", fileName);
    }

    // Content-staging bundles from a real environment are commonly 5–20 MB (they carry
    // full version history and base64-embedded images), against a Kestrel MaxRequestBodySize
    // default of ~28.6 MB. Raised to 64 MB so a legitimate export is not rejected with an
    // empty-body 400 before the controller sees it, and attribute-scoped so the raised limit
    // applies only to the upload endpoint rather than the whole app.
    //
    // Only Preview needs it. Confirm posts a session id, so the FormOptions ValueLengthLimit
    // that used to gate the round-tripped bundle no longer has anything large to carry.
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

        // Buffer the upload before decoding it. The 50 MB cap above already bounds this, and the
        // bytes have to be in hand either way to tell a zipped bundle from a plain one.
        using var uploaded = new MemoryStream();
        await bundle.CopyToAsync(uploaded);

        string json;
        try
        {
            // Sniffs zip vs JSON rather than trusting the extension, decodes as strict UTF-8
            // (invalid sequences are rejected rather than silently becoming U+FFFD, which would
            // corrupt content invisibly and could land malformed strings in the DB), and bounds
            // the decompressed size so a small hostile zip cannot expand to fill memory.
            json = ContentBundleArchive.ReadBundleJson(uploaded.ToArray(), MaxBundleBytes);
        }
        catch (System.Text.DecoderFallbackException)
        {
            TempData["ContentStagingError"] =
                "Bundle file contains invalid UTF-8 bytes. Re-export from a supported environment.";
            return Redirect("/admin/content-staging");
        }
        catch (ContentBundleFormatException ex)
        {
            TempData["ContentStagingError"] = ex.Message;
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
                "Bundle failed validation:\n" + Banner(ex.Issues.Select(i => "• " + i.Message).ToList());
            return Redirect("/admin/content-staging");
        }
        // Sweep before storing. A session table that only ever holds in-flight previews needs no
        // scheduler to bound it: the one action that adds a row is also the one that can afford
        // to drop the rows nobody came back for.
        await sessions.PurgeExpiredAsync();

        var sessionId = await sessions.CreateAsync(
            ContentStagingJson.Serialize(parsed!), currentUser.Email);

        return View(new ImportPreviewViewModel
        {
            Preview = preview,
            SessionId = sessionId
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
    // overrides. The body is a session id plus the mode radios, so it stays small no matter how
    // big the bundle is — the bundle itself never left the server.
    // The body is small — a session id and the mode radios — but it is neither short nor
    // self-limiting, and all three ceilings have to agree or a bundle that validated and
    // previewed cleanly dies on confirm:
    //
    //  * Field count. The preview renders a row per item and each row posts two fields, so a
    //    bundle of a few hundred items runs past FormOptions.ValueCountLimit's default of 1024.
    //    With the bundle no longer round-tripping, field COUNT is the binding ceiling where
    //    field SIZE used to be.
    //  * Collection size. MvcOptions.MaxModelBindingCollectionSize caps the bound Decisions
    //    list at 1024 by default, and exceeding it throws rather than adding a model error — a
    //    500, not a 400. Raised to match in CoreWebExtensions.
    //  * Body size. Kestrel's MaxRequestBodySize is disabled application-wide, so without an
    //    explicit limit here this endpoint accepts an unbounded body.
    [HttpPost("import")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(ContentStagingFormLimits.MaxConfirmBodyBytes)]
    [RequestFormLimits(ValueCountLimit = ContentStagingFormLimits.MaxFormValues)]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Import(ImportConfirmFormModel model)
    {
        ContentBundle? parsed = null;
        string? error = null;
        var bundleJson = await sessions.GetBundleJsonAsync(model.SessionId);
        if (string.IsNullOrEmpty(bundleJson) || !TryParseBundle(bundleJson, out parsed, out error))
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
                TempData["ContentStagingError"] = Banner(result.Errors);
            if (result.Warnings.Count > 0)
                TempData["ContentStagingWarnings"] = Banner(result.Warnings);

            // Forensic trail — every bulk destructive admin action lands in AuditEntries so
            // an incident-responder can reconstruct "who imported what, when" without
            // grepping app logs. Only successful imports are recorded (failed imports have
            // no net effect worth tracing). Matches QueueAdminController's audit pattern.
            //
            // Outside the session delete below, and swallowing its own failure: the import has
            // already been applied at this point, so letting an audit-write error reach the
            // outer catch would report "import failed" for an import that succeeded, and leave
            // the session inviting a re-run.
            try
            {
                await WriteImportAuditAsync(result, bundleJson!.Length);
            }
            catch (Exception auditEx)
            {
                logger.LogError(auditEx, "Import controller: the import succeeded but its audit entry could not be written");
            }

            // The session is a ticket, spent once the bundle has fully landed. It is kept
            // whenever anything went wrong — including per-item errors, which leave part of the
            // bundle unapplied — so the operator can fix the cause and confirm again without
            // re-uploading something that may have taken minutes to transfer.
            if (result.Errors.Count == 0)
            {
                await sessions.DeleteAsync(model.SessionId);
            }
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
                "Bundle failed validation on import:\n" + Banner(ex.Issues.Select(i => "• " + i.Message).ToList());
        }
        catch (Exception ex)
        {
            // The service handles per-item failures internally, so this catches only the
            // catastrophic-outer-flow case — e.g. a database connection failure or a JSON edge case
            // that the parser missed. Log the full exception so the review-app logs surface it,
            // and hand the user a coherent error instead of a blank 500 page.
            logger.LogError(ex, "Import controller: bundle import failed with an unhandled exception");
            // Through Banner like every other message: a database exception routinely quotes the
            // offending statement and parameters, which can carry the bundle's own content.
            TempData["ContentStagingError"] = Banner(
                [$"Import failed: {ex.GetType().Name} — {ex.Message}. Check the application logs for the full stack trace."]);
        }
        finally
        {
            // Always release — even if the import threw, we must free the lock for the
            // next caller. Advisory-lock release is scoped to the session we acquired on,
            // so a peer's separate session is unaffected either way.
            //
            // Its own failure is swallowed for the same reason the audit write's is: by this
            // point the import has been applied, and letting an exception out of the finally
            // would discard the redirect and the result banner and hand the operator a 500 for
            // an import that succeeded. A lock left held is recovered when the connection
            // closes at the end of the request, which drops session-scoped advisory locks.
            if (lockAcquired && _importLock is not null)
            {
                try
                {
                    await _importLock.ReleaseAsync();
                }
                catch (Exception releaseEx)
                {
                    logger.LogError(releaseEx, "Import controller: the import finished but its advisory lock could not be released");
                }
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

    // Bounds on anything that reaches a banner.
    //
    // TempData here is cookie-backed, so whatever goes in comes back up as request headers on
    // the next request. Past Kestrel's header limit that is a 431 for that browser until its
    // cookies are cleared — self-inflicted, and triggerable by uploading one junk file.
    //
    // Both a count and a character budget are needed, and the budget is the one that matters.
    // Validator and import messages embed the offending Title, Segment or Key verbatim, and
    // none of those is length-limited — a segment is quoted back precisely because it failed
    // the format rule, so its content is the uploader's choice. Twenty messages is therefore no
    // protection at all if one of them is two megabytes.
    private const int MaxBannerMessages = 20;
    private const int MaxBannerChars = 2048;
    private const int MaxBannerMessageChars = 200;

    private static string Banner(IReadOnlyCollection<string> messages)
    {
        var shown = messages.Take(MaxBannerMessages).Select(Clip).ToList();
        var omitted = messages.Count - shown.Count;

        var text = string.Join("\n", shown);
        if (text.Length > MaxBannerChars)
        {
            text = text[..MaxBannerChars] + "…";
        }

        return omitted > 0
            ? text + $"\n… and {omitted} more. See the application logs for the full list."
            : text;
    }

    private static string Clip(string message) =>
        message.Length <= MaxBannerMessageChars ? message : message[..MaxBannerMessageChars] + "…";

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
