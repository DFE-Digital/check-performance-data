using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.Application;
using DfE.CheckPerformanceData.Application.Wiki;
using DfE.CheckPerformanceData.Domain.Entities;
using Ganss.Xss;
using Markdig;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Infrastructure.Wiki;

public partial class WikiService(IPortalDbContext context) : IWikiService
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private static readonly HtmlSanitizer Sanitizer = new();

    public async Task<List<WikiPageTreeNodeDto>> GetNavigationTreeAsync()
    {
        var pages = await context.WikiPages
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Title)
            .Select(p => new { p.Id, p.Title, p.Slug, p.ParentId, p.SortOrder })
            .ToListAsync();

        var lookup = pages.ToLookup(p => p.ParentId);

        List<WikiPageTreeNodeDto> BuildChildren(int? parentId, string parentSlugPath)
        {
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
                        Children = BuildChildren(p.Id, slugPath)
                    };
                })
                .ToList();
        }

        return BuildChildren(null, string.Empty);
    }

    public async Task<WikiPageDto?> GetPageBySlugPathAsync(string slugPath)
    {
        var slugs = slugPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (slugs.Length == 0) return null;

        int? parentId = null;
        WikiPage? page = null;

        foreach (var slug in slugs)
        {
            page = await context.WikiPages
                .FirstOrDefaultAsync(p => p.Slug == slug && p.ParentId == parentId);

            if (page == null) return null;
            parentId = page.Id;
        }

        return page == null ? null : ToDto(page, slugPath);
    }

    public async Task<WikiPageDto?> GetPageByIdAsync(int id)
    {
        var page = await context.WikiPages.FindAsync(id);
        if (page == null) return null;

        var slugPath = await BuildSlugPathAsync(page);
        return ToDto(page, slugPath);
    }

    public async Task<WikiPageDto> CreatePageAsync(CreateWikiPageDto dto)
    {
        var slug = GenerateSlug(dto.Title);

        var existingSlug = await context.WikiPages
            .AnyAsync(p => p.Slug == slug && p.ParentId == dto.ParentId);

        if (existingSlug)
            slug = $"{slug}-{DateTime.UtcNow.Ticks}";

        var maxSortOrder = await context.WikiPages
            .Where(p => p.ParentId == dto.ParentId)
            .MaxAsync(p => (int?)p.SortOrder) ?? -1;

        var page = new WikiPage
        {
            Title = dto.Title,
            Slug = slug,
            Content = dto.Content,
            ParentId = dto.ParentId,
            SortOrder = maxSortOrder + 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.WikiPages.Add(page);
        await context.SaveChangesAsync();

        var version = new WikiPageVersion
        {
            WikiPageId = page.Id,
            Title = page.Title,
            Content = page.Content,
            VersionNumber = 1,
            CreatedAt = DateTime.UtcNow
        };

        context.WikiPageVersions.Add(version);
        await context.SaveChangesAsync();

        var slugPath = await BuildSlugPathAsync(page);
        return ToDto(page, slugPath);
    }

    public async Task<WikiPageDto> UpdatePageAsync(int id, UpdateWikiPageDto dto)
    {
        var page = await context.WikiPages.FindAsync(id)
            ?? throw new InvalidOperationException($"Wiki page {id} not found.");

        page.Title = dto.Title;
        page.Content = dto.Content;
        page.Slug = GenerateSlug(dto.Title);
        page.UpdatedAt = DateTime.UtcNow;

        var maxVersion = await context.WikiPageVersions
            .Where(v => v.WikiPageId == id)
            .MaxAsync(v => (int?)v.VersionNumber) ?? 0;

        var version = new WikiPageVersion
        {
            WikiPageId = id,
            Title = dto.Title,
            Content = dto.Content,
            VersionNumber = maxVersion + 1,
            CreatedAt = DateTime.UtcNow
        };

        context.WikiPageVersions.Add(version);
        await context.SaveChangesAsync();

        var slugPath = await BuildSlugPathAsync(page);
        return ToDto(page, slugPath);
    }

    public async Task DeletePageAsync(int id)
    {
        var page = await context.WikiPages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new InvalidOperationException($"Wiki page {id} not found.");

        await SoftDeleteRecursiveAsync(page);
        await context.SaveChangesAsync();
    }

    public async Task MovePageAsync(int id, int? newParentId, int newSortOrder)
    {
        var page = await context.WikiPages.FindAsync(id)
            ?? throw new InvalidOperationException($"Wiki page {id} not found.");

        var oldParentId = page.ParentId;
        page.ParentId = newParentId;
        page.SortOrder = newSortOrder;

        // Reorder siblings at the new parent
        var siblings = await context.WikiPages
            .Where(p => p.ParentId == newParentId && p.Id != id)
            .OrderBy(p => p.SortOrder)
            .ToListAsync();

        var order = 0;
        foreach (var sibling in siblings)
        {
            if (order == newSortOrder) order++;
            sibling.SortOrder = order++;
        }

        // Reorder old siblings if parent changed
        if (oldParentId != newParentId)
        {
            var oldSiblings = await context.WikiPages
                .Where(p => p.ParentId == oldParentId && p.Id != id)
                .OrderBy(p => p.SortOrder)
                .ToListAsync();

            for (var i = 0; i < oldSiblings.Count; i++)
                oldSiblings[i].SortOrder = i;
        }

        await context.SaveChangesAsync();
    }

    public async Task<List<WikiPageVersionDto>> GetPageVersionsAsync(int pageId)
    {
        return await context.WikiPageVersions
            .Where(v => v.WikiPageId == pageId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new WikiPageVersionDto
            {
                Id = v.Id,
                VersionNumber = v.VersionNumber,
                Title = v.Title,
                Content = v.Content,
                CreatedAt = v.CreatedAt,
                CreatedBy = v.CreatedBy
            })
            .ToListAsync();
    }

    public async Task<WikiPageDto> RevertToVersionAsync(int pageId, int versionId)
    {
        var page = await context.WikiPages.FindAsync(pageId)
            ?? throw new InvalidOperationException($"Wiki page {pageId} not found.");

        var version = await context.WikiPageVersions.FindAsync(versionId)
            ?? throw new InvalidOperationException($"Wiki page version {versionId} not found.");

        page.Title = version.Title;
        page.Content = version.Content;
        page.Slug = GenerateSlug(version.Title);
        page.UpdatedAt = DateTime.UtcNow;

        var maxVersion = await context.WikiPageVersions
            .Where(v => v.WikiPageId == pageId)
            .MaxAsync(v => v.VersionNumber);

        var newVersion = new WikiPageVersion
        {
            WikiPageId = pageId,
            Title = version.Title,
            Content = version.Content,
            VersionNumber = maxVersion + 1,
            CreatedAt = DateTime.UtcNow
        };

        context.WikiPageVersions.Add(newVersion);
        await context.SaveChangesAsync();

        var slugPath = await BuildSlugPathAsync(page);
        return ToDto(page, slugPath);
    }

    private async Task SoftDeleteRecursiveAsync(WikiPage page)
    {
        page.IsDeleted = true;
        page.UpdatedAt = DateTime.UtcNow;

        var children = await context.WikiPages
            .IgnoreQueryFilters()
            .Where(p => p.ParentId == page.Id && !p.IsDeleted)
            .ToListAsync();

        foreach (var child in children)
            await SoftDeleteRecursiveAsync(child);
    }

    private async Task<string> BuildSlugPathAsync(WikiPage page)
    {
        var slugs = new List<string> { page.Slug };
        var current = page;

        while (current.ParentId.HasValue)
        {
            current = await context.WikiPages.FindAsync(current.ParentId.Value);
            if (current == null) break;
            slugs.Insert(0, current.Slug);
        }

        return string.Join("/", slugs);
    }

    private static WikiPageDto ToDto(WikiPage page, string slugPath) => new()
    {
        Id = page.Id,
        Title = page.Title,
        Slug = page.Slug,
        SlugPath = slugPath,
        Content = page.Content,
        ContentHtml = RenderContentHtml(page.Content),
        ParentId = page.ParentId,
        SortOrder = page.SortOrder
    };

    private static string? RenderContentHtml(string? content)
    {
        if (string.IsNullOrEmpty(content)) return null;

        // If content contains HTML tags, treat as HTML (from WYSIWYG editor)
        // Otherwise treat as markdown
        var html = ContainsHtmlTags().IsMatch(content)
            ? content
            : Markdown.ToHtml(content, MarkdownPipeline);

        return Sanitizer.Sanitize(html);
    }

    [GeneratedRegex(@"<\s*[a-z][a-z0-9]*[\s>]", RegexOptions.IgnoreCase)]
    private static partial Regex ContainsHtmlTags();

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
