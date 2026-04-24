using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Wiki;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class WikiRepository(
    IPortalDbContext context,
    ICurrentUserService currentUserService) : IWikiRepository
{
    // Queries — use ProjectToDto so EF translates the mapping to SQL

    public async Task<List<WikiPageDto>> GetAllOrderedAsync() =>
        await context.WikiPages
            .AsNoTracking()
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Title)
            .ProjectToDto()
            .ToListAsync();

    public async Task<WikiPageDto?> GetByIdAsync(int id) =>
        await context.WikiPages
            .AsNoTracking()
            .Where(p => p.Id == id)
            .ProjectToDto()
            .FirstOrDefaultAsync();

    public async Task<WikiPageDto?> GetByIdIgnoringFiltersAsync(int id) =>
        await context.WikiPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => p.Id == id)
            .ProjectToDto()
            .FirstOrDefaultAsync();

    public async Task<WikiPageDto?> GetByIdIncludingDeletedAsync(int id) =>
        await context.WikiPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => p.Id == id)
            .ProjectToDto()
            .FirstOrDefaultAsync();

    public async Task<WikiPageDto?> GetBySlugAndParentAsync(string slug, int? parentId) =>
        await context.WikiPages
            .AsNoTracking()
            .Where(p => p.Slug == slug && p.ParentId == parentId)
            .ProjectToDto()
            .FirstOrDefaultAsync();

    public async Task<bool> SlugExistsAsync(string slug, int? parentId) =>
        await context.WikiPages.AnyAsync(p => p.Slug == slug && p.ParentId == parentId && !p.IsDeleted);

    public async Task<List<DeletedWikiPageInfo>> GetDeletedRootsAsync()
    {
        var deleted = await context.WikiPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => p.IsDeleted)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.Slug,
                p.ParentId,
                p.DeletedAt,
                p.DeletedBy
            })
            .ToListAsync();

        var deletedIds = deleted.Select(p => p.Id).ToHashSet();

        return deleted
            .Where(p => !p.ParentId.HasValue || !deletedIds.Contains(p.ParentId.Value))
            .Select(p => new DeletedWikiPageInfo
            {
                Id = p.Id,
                Title = p.Title,
                Slug = p.Slug,
                ParentId = p.ParentId,
                DeletedAt = p.DeletedAt,
                DeletedBy = p.DeletedBy
            })
            .ToList();
    }

    public async Task<int> CountDeletedDescendantsAsync(int rootId)
    {
        var deleted = await context.WikiPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => p.IsDeleted)
            .Select(p => new { p.Id, p.ParentId })
            .ToListAsync();

        var byParent = deleted.ToLookup(p => p.ParentId);
        var stack = new Stack<int>();
        stack.Push(rootId);
        var count = 0;

        while (stack.Count > 0)
        {
            var currentId = stack.Pop();
            foreach (var child in byParent[currentId])
            {
                count++;
                stack.Push(child.Id);
            }
        }

        return count;
    }

    public async Task<List<WikiPageDto>> GetLivePagesForParentPickerAsync() =>
        await context.WikiPages
            .AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Title)
            .ProjectToDto()
            .ToListAsync();

    public async Task<int> GetMaxSortOrderAsync(int? parentId) =>
        await context.WikiPages
            .Where(p => p.ParentId == parentId)
            .MaxAsync(p => (int?)p.SortOrder) ?? -1;

    public async Task<List<WikiPageDto>> GetChildrenIgnoringFiltersAsync(int parentId) =>
        await context.WikiPages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => p.ParentId == parentId && !p.IsDeleted)
            .ProjectToDto()
            .ToListAsync();

    public async Task<List<WikiPageDto>> GetSiblingsAsync(int? parentId, int excludeId) =>
        await context.WikiPages
            .AsNoTracking()
            .Where(p => p.ParentId == parentId && p.Id != excludeId)
            .OrderBy(p => p.SortOrder)
            .ProjectToDto()
            .ToListAsync();

    public async Task<int> GetMaxVersionNumberAsync(int wikiPageId) =>
        await context.WikiPageVersions
            .Where(v => v.WikiPageId == wikiPageId)
            .MaxAsync(v => (int?)v.VersionNumber) ?? 0;

    public async Task<WikiPageVersionDto?> GetVersionByIdAsync(int versionId) =>
        await context.WikiPageVersions
            .AsNoTracking()
            .Where(v => v.Id == versionId)
            .ProjectToVersionDto()
            .FirstOrDefaultAsync();

    public async Task<List<WikiPageVersionDto>> GetVersionsByPageIdAsync(int pageId) =>
        await context.WikiPageVersions
            .AsNoTracking()
            .Where(v => v.WikiPageId == pageId)
            .OrderByDescending(v => v.VersionNumber)
            .ProjectToVersionDto()
            .ToListAsync();

    // Queries — search (full-text)

    public async Task<(List<WikiPageSearchResultDto> Items, int Total)> SearchAsync(string query, int skip, int take)
    {
        // EF.Functions.WebSearchToTsQuery MUST appear inline inside each expression tree that
        // uses it — its `config` argument is [NotParameterized], so the Npgsql translator only
        // recognises direct call sites. Hoisting to a local forces client-evaluation, which
        // throws ("method is not supported because the query has switched to client-evaluation").
        // websearch_to_tsquery NEVER raises syntax errors — accepts any input (empty, unbalanced,
        // gibberish). See 05-RESEARCH.md §Investigation 12. Input sanitisation happens at the
        // service layer (trim + length).

        // Soft-delete filter inherited from HasQueryFilter(w => !w.IsDeleted) — DO NOT add IgnoreQueryFilters().
        // See 05-CONTEXT.md D-08 and Threat T-AC-01.
        var matching = context.WikiPages
            .AsNoTracking()
            .Where(p => p.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("english", query)));

        var total = await matching.CountAsync();

        var items = await matching
            .OrderByDescending(p => p.SearchVector.Rank(EF.Functions.WebSearchToTsQuery("english", query)))
            .ThenBy(p => p.Title)   // stable tie-break
            .Skip(skip)
            .Take(take)
            .Select(p => new WikiPageSearchResultDto
            {
                Id = p.Id,
                Title = p.Title,
                Slug = p.Slug,
                ParentId = p.ParentId,
                // ts_headline emits only <mark>/</mark> and source text.
                // SnippetHtml is rendered via @Html.Raw in Search.cshtml — safe because
                // BodyPlainText is tag-stripped at write time (Plans 02/03/05).
                // See T-XSS-01 mitigation in 05-UI-SPEC.md §Security Contract.
                SnippetHtml = EF.Functions.WebSearchToTsQuery("english", query).GetResultHeadline(
                    p.BodyPlainText,
                    "StartSel=<mark>,StopSel=</mark>,MaxWords=25,MinWords=15,ShortWord=3,MaxFragments=1")
            })
            .ToListAsync();

        return (items, total);
    }

    // Commands — work with tracked entities internally

    public async Task<WikiPageDto> AddPageAsync(CreateWikiPageDto dto, string slug, int sortOrder, string bodyPlainText)
    {
        var entity = new WikiPage
        {
            Title = dto.Title,
            Slug = slug,
            Content = dto.Content,
            BodyPlainText = bodyPlainText,
            ParentId = dto.ParentId,
            SortOrder = sortOrder,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.WikiPages.Add(entity);
        await context.SaveChangesAsync();

        return WikiPageMapper.ToDto(entity);
    }

    public async Task AddVersionAsync(int wikiPageId, string title, string? content, int versionNumber)
    {
        var version = new WikiPageVersion
        {
            WikiPageId = wikiPageId,
            Title = title,
            Content = content,
            VersionNumber = versionNumber,
            CreatedAt = DateTime.UtcNow
        };

        context.WikiPageVersions.Add(version);
        await context.SaveChangesAsync();
    }

    public async Task UpdatePageAsync(int id, string title, string? content, string slug, string bodyPlainText)
    {
        var entity = await context.WikiPages.FindAsync(id)
            ?? throw new InvalidOperationException($"Wiki page {id} not found.");

        entity.Title = title;
        entity.Content = content;
        entity.BodyPlainText = bodyPlainText;
        entity.Slug = slug;
        entity.UpdatedAt = DateTime.UtcNow;
    }

    public async Task SoftDeleteRecursiveAsync(int id)
    {
        var entity = await context.WikiPages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new InvalidOperationException($"Wiki page {id} not found.");

        await SoftDeleteRecursiveInternalAsync(entity);
        await context.SaveChangesAsync();
    }

    public async Task RestoreSubtreeAsync(int rootId, int? newParentId, string slug, int sortOrder, string bodyPlainText)
    {
        var root = await context.WikiPages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == rootId && p.IsDeleted)
            ?? throw new InvalidOperationException($"Wiki page {rootId} not found or not deleted.");

        var now = DateTime.UtcNow;
        var user = currentUserService.UserId;

        root.IsDeleted = false;
        root.DeletedAt = null;
        root.DeletedBy = null;
        root.ParentId = newParentId;
        root.Slug = slug;
        root.SortOrder = sortOrder;
        root.BodyPlainText = bodyPlainText;
        root.UpdatedAt = now;
        root.UpdatedBy = user;

        await RestoreDescendantsAsync(root.Id, now, user);
    }

    private async Task RestoreDescendantsAsync(int parentId, DateTime now, string? user)
    {
        var children = await context.WikiPages
            .IgnoreQueryFilters()
            .Where(p => p.ParentId == parentId && p.IsDeleted)
            .ToListAsync();

        foreach (var child in children)
        {
            child.IsDeleted = false;
            child.DeletedAt = null;
            child.DeletedBy = null;
            child.UpdatedAt = now;
            child.UpdatedBy = user;

            await RestoreDescendantsAsync(child.Id, now, user);
        }
    }

    public async Task MovePageAsync(int id, int? newParentId, int newSortOrder)
    {
        var entity = await context.WikiPages.FindAsync(id)
            ?? throw new InvalidOperationException($"Wiki page {id} not found.");

        entity.ParentId = newParentId;
        entity.SortOrder = newSortOrder;
    }

    public async Task ReorderSiblingsAsync(int? parentId, int excludeId, int insertAtPosition)
    {
        var siblings = await context.WikiPages
            .Where(p => p.ParentId == parentId && p.Id != excludeId)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();

        var order = 0;
        foreach (var sibling in siblings)
        {
            if (order == insertAtPosition) order++;
            sibling.SortOrder = order++;
        }
    }

    public async Task ReorderSiblingsSequentialAsync(int? parentId, int excludeId)
    {
        var siblings = await context.WikiPages
            .Where(p => p.ParentId == parentId && p.Id != excludeId)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();

        for (var i = 0; i < siblings.Count; i++)
            siblings[i].SortOrder = i;
    }

    public async Task SaveChangesAsync() =>
        await context.SaveChangesAsync();

    public async Task ExecuteInTransactionAsync(Func<Task> work) =>
        await context.ExecuteInTransactionAsync(work);

    private async Task SoftDeleteRecursiveInternalAsync(WikiPage page)
    {
        var now = DateTime.UtcNow;
        var user = currentUserService.UserId;

        page.IsDeleted = true;
        page.DeletedAt = now;
        page.DeletedBy = user;
        page.UpdatedAt = now;
        page.UpdatedBy = user;

        var children = await context.WikiPages
            .IgnoreQueryFilters()
            .Where(p => p.ParentId == page.Id && !p.IsDeleted)
            .ToListAsync();

        foreach (var child in children)
            await SoftDeleteRecursiveInternalAsync(child);
    }
}
