using DfE.CheckPerformanceData.Application.Wiki;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class HelpController(IWikiService wikiService) : Controller
{
    private bool IsEditMode => 
        Request.Query.ContainsKey("edit") || (Request.HasFormContentType && Request.Form.ContainsKey("editMode"));

    private string EditSuffix => IsEditMode ? "?edit" : "";

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

        var page = await wikiService.CreatePageAsync(dto);
        return Redirect($"/help/{page.SlugPath}{EditSuffix}");
    }

    [HttpPost("help/edit/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EditWikiPageViewModel model)
    {
        if (!ModelState.IsValid)
            return Redirect($"/help{EditSuffix}");

        var dto = new UpdateWikiPageDto
        {
            Title = model.Title,
            Content = model.Content
        };

        var page = await wikiService.UpdatePageAsync(id, dto);
        return Redirect($"/help/{page.SlugPath}");
    }

    [HttpPost("help/delete/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        await wikiService.DeletePageAsync(id);
        return Redirect($"/help{EditSuffix}");
    }

    [HttpPost("help/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move([FromBody] MovePageRequest request)
    {
        await wikiService.MovePageAsync(request.Id, request.NewParentId, request.NewSortOrder);
        var page = await wikiService.GetPageByIdAsync(request.Id);
        return Ok(new { slugPath = page?.SlugPath ?? "" });
    }

    [HttpGet("help/search")]
    public async Task<IActionResult> Search(string? q, int page = 1)
    {
        var result = await wikiService.SearchAsync(q ?? string.Empty, page);
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

    [HttpPost("help/restore/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id, int? newParentId)
    {
        var page = await wikiService.RestorePageAsync(id, newParentId);
        return Redirect($"/help/{page.SlugPath}{EditSuffix}");
    }

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

    [HttpPost("help/revert/{pageId:int}/{versionId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revert(int pageId, int versionId)
    {
        var page = await wikiService.RevertToVersionAsync(pageId, versionId);
        return Redirect($"/help/{page.SlugPath}{EditSuffix}");
    }
}

public sealed class MovePageRequest
{
    public int Id { get; set; }
    public int? NewParentId { get; set; }
    public int NewSortOrder { get; set; }
}
