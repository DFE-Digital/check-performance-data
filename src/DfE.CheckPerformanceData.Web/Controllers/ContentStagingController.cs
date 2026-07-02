using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Content staging: editors export the wiki pages + content blocks of one environment as a
// schema-versioned JSON bundle and import it into another. Gated by the content-editor role.
[Authorize(Roles = WikiConstants.EditorRole)]
[Route("admin/content-staging")]
public sealed class ContentStagingController(
    IContentStagingService staging,
    ICurrentUserService currentUser) : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();

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

    // Step 1 of import: read the uploaded file, analyse it against the current environment, and
    // show the preview so the administrator can see new vs colliding content and decide per item.
    [HttpPost("preview")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(IFormFile? bundle)
    {
        if (bundle is null || bundle.Length == 0)
        {
            TempData["ContentStagingError"] = "Choose a bundle file to import.";
            return Redirect("/admin/content-staging");
        }

        string json;
        using (var reader = new StreamReader(bundle.OpenReadStream()))
        {
            json = await reader.ReadToEndAsync();
        }

        if (!TryParseBundle(json, out var parsed, out var error))
        {
            TempData["ContentStagingError"] = error;
            return Redirect("/admin/content-staging");
        }

        var preview = await staging.PreviewAsync(parsed!);
        return View(new ImportPreviewViewModel
        {
            Preview = preview,
            BundleJson = ContentStagingJson.Serialize(parsed!)
        });
    }

    // Step 2 of import: apply the previewed bundle with the chosen global mode and any per-item
    // overrides. The bundle round-trips through a hidden field so no server-side state is needed.
    [HttpPost("import")]
    [ValidateAntiForgeryToken]
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

        try
        {
            var result = await staging.ImportAsync(parsed!, model.GlobalMode, decisions);
            TempData["ContentStagingResult"] = BuildSummary(result);
            if (result.Errors.Count > 0)
                TempData["ContentStagingError"] = string.Join("\n", result.Errors);
            if (result.Warnings.Count > 0)
                TempData["ContentStagingWarnings"] = string.Join("\n", result.Warnings);
        }
        catch (ContentImportConflictException ex)
        {
            TempData["ContentStagingError"] = ex.Message;
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
}
