using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Wiki;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class WikiRepository(PortalDbContext context) : IWikiRepository
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

    public async Task<WikiPageDto?> GetBySlugAndParentAsync(string slug, int? parentId) =>
        await context.WikiPages
            .AsNoTracking()
            .Where(p => p.Slug == slug && p.ParentId == parentId)
            .ProjectToDto()
            .FirstOrDefaultAsync();

    public async Task<bool> SlugExistsAsync(string slug, int? parentId) =>
        await context.WikiPages.AnyAsync(p => p.Slug == slug && p.ParentId == parentId);

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

    // Commands — work with tracked entities internally

    public async Task<WikiPageDto> AddPageAsync(CreateWikiPageDto dto, string slug, int sortOrder)
    {
        var entity = new WikiPage
        {
            Title = dto.Title,
            Slug = slug,
            Content = dto.Content,
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

    public async Task UpdatePageAsync(int id, string title, string? content, string slug)
    {
        var entity = await context.WikiPages.FindAsync(id)
            ?? throw new InvalidOperationException($"Wiki page {id} not found.");

        entity.Title = title;
        entity.Content = content;
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
        page.IsDeleted = true;
        page.UpdatedAt = DateTime.UtcNow;

        var children = await context.WikiPages
            .IgnoreQueryFilters()
            .Where(p => p.ParentId == page.Id && !p.IsDeleted)
            .ToListAsync();

        foreach (var child in children)
            await SoftDeleteRecursiveInternalAsync(child);
    }
}
