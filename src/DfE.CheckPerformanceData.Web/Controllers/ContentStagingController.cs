using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.CurrentUser;
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
    public async Task<IActionResult> ExportSelected(List<Guid>? wikiPageIds, List<Guid>? contentBlockIds)
    {
        var selection = new ContentExportSelection(
            wikiPageIds?.ToHashSet() ?? [],
            contentBlockIds?.ToHashSet() ?? []);

        if (selection.WikiPageIds.Count == 0 && selection.ContentBlockIds.Count == 0)
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
            WikiPages = bundle.WikiPages,
            ContentBlocks = bundle.ContentBlocks
        };

        var bytes = Encoding.UTF8.GetBytes(ContentStagingJson.Serialize(stamped));
        var fileName = $"cpd-content-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
        return File(bytes, "application/json", fileName);
    }

    [HttpPost("import")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(IFormFile? bundle, ContentImportMode mode)
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

        ContentBundle? parsed;
        try
        {
            parsed = ContentStagingJson.Deserialize(json);
        }
        catch (JsonException)
        {
            parsed = null;
        }

        if (parsed is null || parsed.Schema != ContentBundle.CurrentSchema)
        {
            TempData["ContentStagingError"] =
                $"The file is not a valid '{ContentBundle.CurrentSchema}' content bundle.";
            return Redirect("/admin/content-staging");
        }

        if (parsed.SchemaVersion != ContentBundle.CurrentSchemaVersion)
        {
            TempData["ContentStagingError"] =
                $"This bundle is schema version {parsed.SchemaVersion}, but this environment only " +
                $"supports version {ContentBundle.CurrentSchemaVersion}.";
            return Redirect("/admin/content-staging");
        }

        try
        {
            var result = await staging.ImportAsync(parsed, mode);
            TempData["ContentStagingResult"] = BuildSummary(result);
            if (result.Warnings.Count > 0)
                TempData["ContentStagingWarnings"] = string.Join("\n", result.Warnings);
        }
        catch (ContentImportConflictException ex)
        {
            TempData["ContentStagingError"] = ex.Message;
        }

        return Redirect("/admin/content-staging");
    }

    private static string BuildSummary(ContentImportResult r) =>
        $"Import complete. Wiki pages: {r.WikiPagesCreated} added, {r.WikiPagesUpdated} updated, " +
        $"{r.WikiPagesSkipped} skipped. Content blocks: {r.ContentBlocksCreated} added, " +
        $"{r.ContentBlocksUpdated} updated, {r.ContentBlocksSkipped} skipped.";
}
