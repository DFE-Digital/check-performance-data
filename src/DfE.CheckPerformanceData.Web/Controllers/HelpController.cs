using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Application.Wiki;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class HelpController(
    IWikiService wikiService,
    WikiSeeder wikiSeeder,
    ISettingService settingService,
    IContentBlockSearchService contentBlockSearchService,
    ILogger<HelpController> logger) : Controller
{
    private bool IsEditMode =>
        (Request.Query.ContainsKey(WikiConstants.EditQueryKey)
         || (Request.HasFormContentType && Request.Form.ContainsKey(WikiConstants.EditModeFormKey)))
        && User.IsInRole(WikiConstants.EditorRole);

    private string EditSuffix => IsEditMode ? "?" + WikiConstants.EditQueryKey : "";

    // Wiki Index retired: /help and /help/{slugPath} resolve via PageController's catch-all
    // against the PageNode tree. Wiki management endpoints (create/edit/delete/move/search/
    // deleted/versions/restore/revert/seed) remain on this controller — they use explicit
    // HttpGet/HttpPost attribute routes so they're unaffected by the missing Index action.

    [Authorize(Roles = WikiConstants.EditorRole)]
    [HttpPost("help/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateWikiPageViewModel model)
    {
        if (!ModelState.IsValid)
            return Redirect($"/help{EditSuffix}");

        var dto = new CreateWikiPageDto
        {
            Title = model.Title,
            Content = model.Content,
            ParentId = model.ParentId,
            Slug = model.Slug
        };

        try
        {
            var page = await wikiService.CreatePageAsync(dto);
            return Redirect($"/help/{page.SlugPath}{EditSuffix}");
        }
        catch (DuplicateWikiPageException ex)
        {
            TempData["WikiCreateError"] = ex.Message;
            TempData["WikiCreateAttemptedTitle"] = model.Title;
            TempData["WikiCreateAttemptedParentId"] = model.ParentId;
            return Redirect($"/help{EditSuffix}");
        }
    }

    [Authorize(Roles = WikiConstants.EditorRole)]
    [HttpPost("help/edit/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EditWikiPageViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["WikiEditError"] = string.Join("; ",
                ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            TempData["WikiEditAttemptedTitle"] = model.Title;
            TempData["WikiEditAttemptedContent"] = model.Content;
            var existing = await wikiService.GetPageByIdAsync(id);
            var slug = existing?.SlugPath ?? string.Empty;
            return Redirect(string.IsNullOrEmpty(slug)
                ? $"/help{EditSuffix}"
                : $"/help/{slug}{EditSuffix}");
        }

        var dto = new UpdateWikiPageDto
        {
            Title = model.Title,
            Content = model.Content,
            Slug = model.Slug
        };

        try
        {
            var page = await wikiService.UpdatePageAsync(id, dto);
            return Redirect($"/help/{page.SlugPath}{EditSuffix}");
        }
        catch (DuplicateWikiPageException ex)
        {
            TempData["WikiEditError"] = ex.Message;
            TempData["WikiEditAttemptedTitle"] = model.Title;
            var existing = await wikiService.GetPageByIdAsync(id);
            var slug = existing?.SlugPath ?? string.Empty;
            return Redirect(string.IsNullOrEmpty(slug)
                ? $"/help{EditSuffix}"
                : $"/help/{slug}{EditSuffix}");
        }
    }

    [Authorize(Roles = WikiConstants.EditorRole)]
    [HttpPost("help/delete/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        await wikiService.DeletePageAsync(id);
        return Redirect($"/help{EditSuffix}");
    }

    [Authorize(Roles = WikiConstants.EditorRole)]
    [HttpPost("help/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move([FromBody] MovePageRequest? request)
    {
        if (request is null) return BadRequest();
        if (!ModelState.IsValid) return BadRequest(ModelState);
        await wikiService.MovePageAsync(request.Id, request.NewParentId, request.NewSortOrder);
        var page = await wikiService.GetPageByIdAsync(request.Id);
        return Ok(new { slugPath = page?.SlugPath ?? "" });
    }

    [AllowAnonymous]
    [HttpGet("help/search")]
    public async Task<IActionResult> Search(string? q, int page = 1)
    {
        var safePage = page < 1 ? 1 : page;
        var pageSize = await settingService.GetIntAsync(SettingKeys.WikiPageLength);
        var result = await wikiService.SearchAsync(q ?? string.Empty, safePage, pageSize);
        var contentResults = await contentBlockSearchService.SearchAsync(q);
        var tree = await wikiService.GetNavigationTreeAsync() ?? [];

        var errors = result.InvalidReason switch
        {
            SearchInvalidReason.EmptyQuery => new List<string> { "Enter a search term" },
            SearchInvalidReason.BelowMinimumLength => new List<string> { "Enter at least 2 characters" },
            _ => new List<string>()
        };

        var vm = new SearchResultsViewModel
        {
            CurrentQuery = result.Query,
            CurrentPage = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount,
            Results = result.Items,
            ContentResults = contentResults,
            InvalidReason = result.InvalidReason,
            ErrorMessages = errors,
            InputId = "search-q",
            NavigationTree = tree
        };

        return View(vm);
    }

    // Wiki-flavored Deleted / Restore / hard-delete routes have been retired: the CMS now
    // uses /admin/pages/deleted via DeletedPagesController against the PageNode tree.

    [Authorize(Roles = WikiConstants.EditorRole)]
    [HttpGet("help/versions/{id:int}")]
    public async Task<IActionResult> Versions(int id)
    {
        var page = await wikiService.GetPageByIdAsync(id);
        if (page == null) return NotFound();

        var versions = await wikiService.GetPageVersionsAsync(id);

        var vm = new WikiPageVersionsViewModel
        {
            Page = page,
            Versions = versions
        };

        return View(vm);
    }

    [Authorize(Roles = WikiConstants.EditorRole)]
    [HttpPost("help/revert/{pageId:int}/{versionId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revert(int pageId, int versionId)
    {
        var page = await wikiService.RevertToVersionAsync(pageId, versionId);
        return Redirect($"/help/{page.SlugPath}{EditSuffix}");
    }

    [Authorize(Roles = WikiConstants.EditorRole)]
    [HttpPost("help/seed")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Seed()
    {
        var created = await wikiSeeder.SeedAsync();
        TempData["SeedResult"] = created switch
        {
            0 => "Sample pages are already present. Nothing was added.",
            1 => "Added 1 sample page.",
            _ => $"Added {created} sample pages."
        };
        return Redirect("/help");
    }

}

public sealed class MovePageRequest
{
    public int Id { get; set; }
    public int? NewParentId { get; set; }
    public int NewSortOrder { get; set; }
}
