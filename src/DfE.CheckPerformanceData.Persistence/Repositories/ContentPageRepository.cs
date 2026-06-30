using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class ContentPageRepository(IPortalDbContext context) : IContentPageRepository
{
    public async Task<ContentPageDto> CreateAsync(
        string slug, string title, string? caption, string layout, string draftContent, Guid? contentId = null)
    {
        var now = DateTime.UtcNow;
        var entity = new ContentPage
        {
            Slug = slug,
            Title = title,
            Caption = caption,
            Layout = layout,
            DraftContent = draftContent,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Preserve a supplied cross-environment identity; otherwise the DB default generates one.
        if (contentId is { } id && id != Guid.Empty)
            entity.ContentId = id;

        context.ContentPages.Add(entity);
        await context.SaveChangesAsync();

        return ToDto(entity);
    }

    public Task<List<ContentPageSummaryDto>> GetAllAsync() =>
        context.ContentPages
            .AsNoTracking()
            .OrderBy(p => p.Title)
            .Select(p => new ContentPageSummaryDto
            {
                Slug = p.Slug,
                Title = p.Title,
                Layout = p.Layout,
                PublishedVersionNumber = p.PublishedVersionNumber,
                UpdatedAt = p.UpdatedAt
            })
            .ToListAsync();

    public Task<ContentPageDto?> GetBySlugAsync(string slug) =>
        context.ContentPages
            .AsNoTracking()
            .Where(p => p.Slug == slug)
            .Select(Projection)
            .FirstOrDefaultAsync();

    public async Task UpdateDraftAsync(int id, string draftContent)
    {
        var entity = await context.ContentPages.FindAsync(id)
            ?? throw new InvalidOperationException($"Content page {id} not found.");

        entity.DraftContent = draftContent;
        entity.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }

    public async Task<int> GetMaxVersionNumberAsync(int contentPageId) =>
        await context.ContentPageVersions
            .Where(v => v.ContentPageId == contentPageId)
            .MaxAsync(v => (int?)v.VersionNumber) ?? 0;

    public async Task AddVersionAsync(int contentPageId, int versionNumber, string snapshot, string title)
    {
        context.ContentPageVersions.Add(new ContentPageVersion
        {
            ContentPageId = contentPageId,
            VersionNumber = versionNumber,
            Snapshot = snapshot,
            Title = title,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }

    public async Task SetPublishedVersionAsync(int id, int versionNumber)
    {
        var entity = await context.ContentPages.FindAsync(id)
            ?? throw new InvalidOperationException($"Content page {id} not found.");

        entity.PublishedVersionNumber = versionNumber;
        entity.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }

    public Task<ContentPageVersionDto?> GetVersionAsync(int contentPageId, int versionNumber) =>
        context.ContentPageVersions
            .AsNoTracking()
            .Where(v => v.ContentPageId == contentPageId && v.VersionNumber == versionNumber)
            .Select(VersionProjection)
            .FirstOrDefaultAsync();

    public Task<List<ContentPageVersionDto>> GetVersionsAsync(int contentPageId) =>
        context.ContentPageVersions
            .AsNoTracking()
            .Where(v => v.ContentPageId == contentPageId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(VersionProjection)
            .ToListAsync();

    public Task ExecuteInTransactionAsync(Func<Task> work) =>
        context.ExecuteInTransactionAsync(work);

    private static readonly System.Linq.Expressions.Expression<Func<ContentPage, ContentPageDto>> Projection =
        p => new ContentPageDto
        {
            Id = p.Id,
            ContentId = p.ContentId,
            Slug = p.Slug,
            Title = p.Title,
            Caption = p.Caption,
            Layout = p.Layout,
            DraftContent = p.DraftContent,
            PublishedVersionNumber = p.PublishedVersionNumber,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        };

    private static readonly System.Linq.Expressions.Expression<Func<ContentPageVersion, ContentPageVersionDto>> VersionProjection =
        v => new ContentPageVersionDto
        {
            Id = v.Id,
            VersionNumber = v.VersionNumber,
            Snapshot = v.Snapshot,
            Title = v.Title,
            CreatedAt = v.CreatedAt,
            CreatedBy = v.CreatedBy
        };

    private static ContentPageDto ToDto(ContentPage p) => new()
    {
        Id = p.Id,
        ContentId = p.ContentId,
        Slug = p.Slug,
        Title = p.Title,
        Caption = p.Caption,
        Layout = p.Layout,
        DraftContent = p.DraftContent,
        PublishedVersionNumber = p.PublishedVersionNumber,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt
    };
}
