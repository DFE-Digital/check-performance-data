using DfE.CheckPerformanceData.Application.Wiki;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class HelpController(
    IWikiService wikiService,
    WikiSeeder wikiSeeder,
    ILogger<HelpController> logger) : Controller
{
    private bool IsEditMode =>
        (Request.Query.ContainsKey(WikiConstants.EditQueryKey)
         || (Request.HasFormContentType && Request.Form.ContainsKey(WikiConstants.EditModeFormKey)))
        && User.IsInRole(WikiConstants.EditorRole);

    private string EditSuffix => IsEditMode ? "?" + WikiConstants.EditQueryKey : "";

    [AllowAnonymous]
    public async Task<IActionResult> Index(string? slugPath)
    {
        var tree = await wikiService.GetNavigationTreeAsync();
        WikiPageDto? page = null;

        if (!string.IsNullOrEmpty(slugPath))
        {
            page = await wikiService.GetPageBySlugPathAsync(slugPath);
            if (page == null) return NotFound();
        }
        else if (tree.Count > 0)
        {
            page = await wikiService.GetPageByIdAsync(tree[0].Id);
        }

        var vm = new HelpViewModel
        {
            NavigationTree = tree,
            CurrentPage = page,
            CurrentSlugPath = page?.SlugPath ?? string.Empty,
            IsEditMode = IsEditMode
        };

        return View(vm);
    }

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
            ParentId = model.ParentId
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
            Content = model.Content
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
        var result = await wikiService.SearchAsync(q ?? string.Empty, safePage);
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
            InvalidReason = result.InvalidReason,
            ErrorMessages = errors,
            InputId = "search-q",
            NavigationTree = tree
        };

        return View(vm);
    }

    [Authorize(Roles = WikiConstants.EditorRole)]
    [HttpGet("help/deleted")]
    public async Task<IActionResult> Deleted()
    {
        var deletedPages = await wikiService.GetDeletedPagesAsync();
        var availableParents = await wikiService.GetAvailableParentsAsync();

        var vm = new DeletedWikiPagesViewModel
        {
            DeletedPages = deletedPages,
            AvailableParents = availableParents
        };

        return View(vm);
    }

    [Authorize(Roles = WikiConstants.EditorRole)]
    [HttpPost("help/restore/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id, int? newParentId)
    {
        var page = await wikiService.RestorePageAsync(id, newParentId);
        return Redirect($"/help/{page.SlugPath}{EditSuffix}");
    }

    [Authorize(Roles = WikiConstants.EditorRole)]
    [HttpPost("help/delete-permanently/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePermanently(int id)
    {
        try
        {
            await wikiService.HardDeletePageAsync(id);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Hard-delete refused for page {PageId}", id);
            TempData["HardDeleteError"] = ex.Message;
        }

        return Redirect("/help/deleted");
    }

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
