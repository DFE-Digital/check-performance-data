using System.Text.Encodings.Web;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Application.Wiki;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Test-only surface for the E2E suite. Everything here is editor-gated AND double-gated
// on Dev:ToolsEnabled + !IsProduction (mirroring DevImpersonationController and the other
// sibling Dev*Controllers) so it 404s on any real environment. Groups together:
//
//   * GET  /dev/antiforgery-token — issues the antiforgery cookie + a lone <input>
//     containing the matching token, so the seed helper can POST to editor-gated
//     endpoints without scraping a real form. Replaces /help?edit (which used to render
//     the wiki tree form) after HelpController.Index was retired.
//
//   * GET  /dev/wiki-slug-to-id?slug=... — resolves a wiki page slug to its numeric id.
//     Replaces the old GET /help + regex scrape of _WikiTree.cshtml markup that the seed
//     helper used to convert the slug returned by POST /help/create into the id required
//     by delete/edit/versions.
//
//   * POST /dev/wiki-e2e-cleanup — best-effort teardown that soft-deletes every wiki
//     page whose slug starts with 'e2e-' (the prefix every seeded page carries). Called
//     from PlaywrightFixture.DisposeAsync so orphan pages from a crashed test don't
//     accumulate across CI runs. Replaces the broken SweepOrphanE2eWikiPagesAsync,
//     which scraped a wiki tree that no longer renders.
[Authorize(Roles = WikiConstants.EditorRole)]
public sealed class DevE2eSupportController(
    IConfiguration configuration,
    IHostEnvironment env,
    IWikiService wikiService) : Controller
{
    private const string E2eSlugPrefix = "e2e-";

    private bool IsAllowed =>
        configuration.GetValue<bool>(SettingKeys.DevToolsEnabled)
        && !env.IsProduction();

    [HttpGet("dev/antiforgery-token")]
    public ContentResult AntiforgeryToken([FromServices] IAntiforgery antiforgery)
    {
        if (!IsAllowed) { Response.StatusCode = 404; return Content(string.Empty); }
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        var encoded = HtmlEncoder.Default.Encode(tokens.RequestToken ?? string.Empty);
        return Content(
            $"<input name=\"__RequestVerificationToken\" type=\"hidden\" value=\"{encoded}\" />",
            "text/html");
    }

    [HttpGet("dev/wiki-slug-to-id")]
    public async Task<IActionResult> WikiSlugToId([FromQuery] string slug)
    {
        if (!IsAllowed) return NotFound();
        if (string.IsNullOrWhiteSpace(slug)) return NotFound();
        var page = await wikiService.GetPageBySlugPathAsync(slug);
        return page is null ? NotFound() : Content(page.Id.ToString(), "text/plain");
    }

    // Walks the wiki tree, collects every node whose slug begins with the e2e- prefix,
    // and soft-deletes them one by one. Per-page failures are swallowed so a single
    // stubborn row doesn't abort the sweep — this is teardown-time housekeeping, not a
    // strong-consistency operation.
    [HttpPost("dev/wiki-e2e-cleanup")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> WikiE2eCleanup()
    {
        if (!IsAllowed) return NotFound();

        var tree = await wikiService.GetNavigationTreeAsync();
        var e2eIds = new List<int>();
        Collect(tree, e2eIds);

        var deleted = 0;
        foreach (var id in e2eIds)
        {
            try { await wikiService.DeletePageAsync(id); deleted++; }
            catch { /* best-effort */ }
        }
        return Ok(new { deleted, candidates = e2eIds.Count });
    }

    private static void Collect(IEnumerable<WikiPageTreeNodeDto> nodes, List<int> ids)
    {
        foreach (var node in nodes)
        {
            if (node.Slug.StartsWith(E2eSlugPrefix, StringComparison.Ordinal))
                ids.Add(node.Id);
            if (node.Children is { Count: > 0 })
                Collect(node.Children, ids);
        }
    }
}
