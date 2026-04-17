using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.Application.Common;

namespace DfE.CheckPerformanceData.Application.Wiki;

public sealed partial class WikiService(
    IWikiRepository repository,
    IHtmlRenderingService htmlRenderer) : IWikiService
{
    private const int MaxDepth = 10;

    public async Task<List<WikiPageTreeNodeDto>> GetNavigationTreeAsync()
    {
        var pages = await repository.GetAllOrderedAsync();

        var lookup = pages
            .Select(p => new { p.Id, p.Title, p.Slug, p.ParentId, p.SortOrder })
            .ToLookup(p => p.ParentId);

        List<WikiPageTreeNodeDto> BuildChildren(int? parentId, string parentSlugPath, int depth)
        {
            if (depth >= MaxDepth) return [];

            return lookup[parentId]
                .Select(p =>
                {
                    var slugPath = string.IsNullOrEmpty(parentSlugPath)
                        ? p.Slug
                        : $"{parentSlugPath}/{p.Slug}";

                    return new WikiPageTreeNodeDto
                    {
                        Id = p.Id,
                        Title = p.Title,
                        Slug = p.Slug,
                        SlugPath = slugPath,
                        ParentId = p.ParentId,
                        SortOrder = p.SortOrder,
                        Children = BuildChildren(p.Id, slugPath, depth + 1)
                    };
                })
                .ToList();
        }

        return BuildChildren(null, string.Empty, 0);
    }

    public async Task<WikiPageDto?> GetPageBySlugPathAsync(string slugPath)
    {
        var slugs = slugPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (slugs.Length == 0) return null;

        int? parentId = null;
        WikiPageDto? page = null;

        foreach (var slug in slugs)
        {
            page = await repository.GetBySlugAndParentAsync(slug, parentId);
            if (page == null) return null;
            parentId = page.Id;
        }

        return page == null ? null : EnrichDto(page, slugPath);
    }

    public async Task<WikiPageDto?> GetPageByIdAsync(int id)
    {
        var page = await repository.GetByIdAsync(id);
        if (page == null) return null;

        var slugPath = await BuildSlugPathAsync(page);
        return EnrichDto(page, slugPath);
    }

    public async Task<WikiPageDto> CreatePageAsync(CreateWikiPageDto dto)
    {
        var slug = GenerateSlug(dto.Title);

        if (await repository.SlugExistsAsync(slug, dto.ParentId))
            slug = $"{slug}-{DateTime.UtcNow.Ticks}";

        var maxSortOrder = await repository.GetMaxSortOrderAsync(dto.ParentId);

        WikiPageDto? page = null;
        await repository.ExecuteInTransactionAsync(async () =>
        {
            page = await repository.AddPageAsync(dto, slug, maxSortOrder + 1);
            await repository.AddVersionAsync(page.Id, dto.Title, dto.Content, 1);
        });

        var slugPath = await BuildSlugPathAsync(page!);
        return EnrichDto(page!, slugPath);
    }

    public async Task<WikiPageDto> UpdatePageAsync(int id, UpdateWikiPageDto dto)
    {
        var slug = GenerateSlug(dto.Title);

        await repository.UpdatePageAsync(id, dto.Title, dto.Content, slug);

        var maxVersion = await repository.GetMaxVersionNumberAsync(id);
        await repository.AddVersionAsync(id, dto.Title, dto.Content, maxVersion + 1);

        var page = await repository.GetByIdAsync(id)
            ?? throw new InvalidOperationException($"Wiki page {id} not found.");

        var slugPath = await BuildSlugPathAsync(page);
        return EnrichDto(page, slugPath);
    }

    public async Task DeletePageAsync(int id)
    {
        await repository.SoftDeleteRecursiveAsync(id);
    }

    public async Task MovePageAsync(int id, int? newParentId, int newSortOrder)
    {
        var page = await repository.GetByIdAsync(id)
            ?? throw new InvalidOperationException($"Wiki page {id} not found.");

        var oldParentId = page.ParentId;

        await repository.MovePageAsync(id, newParentId, newSortOrder);
        await repository.ReorderSiblingsAsync(newParentId, id, newSortOrder);

        if (oldParentId != newParentId)
        {
            await repository.ReorderSiblingsSequentialAsync(oldParentId, id);
        }

        await repository.SaveChangesAsync();
    }

    public async Task<List<WikiPageVersionDto>> GetPageVersionsAsync(int pageId)
    {
        var versions = await repository.GetVersionsByPageIdAsync(pageId);

        return versions.Select(v => new WikiPageVersionDto
        {
            Id = v.Id,
            VersionNumber = v.VersionNumber,
            Title = v.Title,
            Content = v.Content,
            ContentHtml = htmlRenderer.RenderHtml(v.Content),
            CreatedAt = v.CreatedAt,
            CreatedBy = v.CreatedBy
        }).ToList();
    }

    public async Task<WikiPageDto> RevertToVersionAsync(int pageId, int versionId)
    {
        var version = await repository.GetVersionByIdAsync(versionId)
            ?? throw new InvalidOperationException($"Wiki page version {versionId} not found.");

        var slug = GenerateSlug(version.Title);

        await repository.UpdatePageAsync(pageId, version.Title, version.Content, slug);

        var maxVersion = await repository.GetMaxVersionNumberAsync(pageId);
        await repository.AddVersionAsync(pageId, version.Title, version.Content, maxVersion + 1);

        var page = await repository.GetByIdAsync(pageId)
            ?? throw new InvalidOperationException($"Wiki page {pageId} not found.");

        var slugPath = await BuildSlugPathAsync(page);
        return EnrichDto(page, slugPath);
    }

    private async Task<string> BuildSlugPathAsync(WikiPageDto page)
    {
        var slugs = new List<string> { page.Slug };
        var currentParentId = page.ParentId;
        var depth = 0;

        while (currentParentId.HasValue && depth < MaxDepth)
        {
            var parent = await repository.GetByIdAsync(currentParentId.Value);
            if (parent == null) break;
            slugs.Insert(0, parent.Slug);
            currentParentId = parent.ParentId;
            depth++;
        }

        return string.Join("/", slugs);
    }

    private WikiPageDto EnrichDto(WikiPageDto page, string slugPath) => new()
    {
        Id = page.Id,
        Title = page.Title,
        Slug = page.Slug,
        SlugPath = slugPath,
        Content = page.Content,
        ContentHtml = htmlRenderer.RenderHtml(page.Content),
        ParentId = page.ParentId,
        SortOrder = page.SortOrder
    };

    private static string GenerateSlug(string title)
    {
        var slug = title.ToLowerInvariant();
        slug = SlugInvalidChars().Replace(slug, "");
        slug = SlugWhitespace().Replace(slug, "-");
        slug = SlugMultipleDashes().Replace(slug, "-");
        return slug.Trim('-');
    }

    [GeneratedRegex(@"[^a-z0-9\s-]")]
    private static partial Regex SlugInvalidChars();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SlugWhitespace();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex SlugMultipleDashes();
}
