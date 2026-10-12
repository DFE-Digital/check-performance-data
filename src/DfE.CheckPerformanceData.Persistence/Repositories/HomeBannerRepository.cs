using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.HomeBanners;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class HomeBannerRepository(IPortalDbContext context, ICurrentUserService currentUser) : IHomeBannerRepository
{
    public async Task<List<HomeBannerDto>> GetAllAsync() =>
        await context.HomeBanners.AsNoTracking()
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Id)
            .Select(b => ToDto(b))
            .ToListAsync();

    public async Task<HomeBannerDto?> GetByIdAsync(int id) =>
        await context.HomeBanners.AsNoTracking().Where(b => b.Id == id).Select(b => ToDto(b)).FirstOrDefaultAsync();

    public async Task<HomeBannerDto?> GetByContentIdAsync(Guid contentId) =>
        await context.HomeBanners.AsNoTracking().Where(b => b.ContentId == contentId).Select(b => ToDto(b)).FirstOrDefaultAsync();

    public async Task<HomeBannerDto> AddAsync(HomeBannerContent content, Guid? contentId = null, int? sortOrder = null)
    {
        var order = sortOrder ?? ((await context.HomeBanners.MaxAsync(b => (int?)b.SortOrder) ?? -1) + 1);
        var now = DateTime.UtcNow;
        var entity = new HomeBanner
        {
            Heading = content.Heading,
            Body = content.Body,
            IsEnabled = content.IsEnabled,
            ShowFrom = content.ShowFrom,
            ShowUntil = content.ShowUntil,
            SortOrder = order,
            CreatedAt = now,
            CreatedBy = currentUser.Email,
            UpdatedAt = now,
            UpdatedBy = currentUser.Email
        };
        if (contentId is { } id && id != Guid.Empty) entity.ContentId = id;

        context.HomeBanners.Add(entity);
        await context.SaveChangesAsync();
        return ToDto(entity);
    }

    public async Task UpdateAsync(int id, HomeBannerContent content)
    {
        var entity = await context.HomeBanners.FirstOrDefaultAsync(b => b.Id == id)
            ?? throw new InvalidOperationException($"Home banner {id} not found.");
        entity.Heading = content.Heading;
        entity.Body = content.Body;
        entity.IsEnabled = content.IsEnabled;
        entity.ShowFrom = content.ShowFrom;
        entity.ShowUntil = content.ShowUntil;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = currentUser.Email;
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var entity = await context.HomeBanners.FirstOrDefaultAsync(b => b.Id == id);
        if (entity is null) return;
        context.HomeBanners.Remove(entity);   // versions cascade
        await context.SaveChangesAsync();
    }

    public async Task SetSortOrdersAsync(IReadOnlyList<(int Id, int SortOrder)> orders)
    {
        var ids = orders.Select(o => o.Id).ToList();
        var entities = await context.HomeBanners.Where(b => ids.Contains(b.Id)).ToListAsync();
        foreach (var (id, sortOrder) in orders)
        {
            var e = entities.FirstOrDefault(b => b.Id == id);
            if (e is not null) e.SortOrder = sortOrder;
        }
        await context.SaveChangesAsync();
    }

    public async Task<int> GetMaxVersionNumberAsync(int homeBannerId) =>
        await context.HomeBannerVersions.Where(v => v.HomeBannerId == homeBannerId).MaxAsync(v => (int?)v.VersionNumber) ?? 0;

    public async Task AddVersionAsync(int homeBannerId, HomeBannerContent snapshot, int versionNumber)
    {
        context.HomeBannerVersions.Add(new HomeBannerVersion
        {
            HomeBannerId = homeBannerId,
            VersionNumber = versionNumber,
            Heading = snapshot.Heading,
            Body = snapshot.Body,
            IsEnabled = snapshot.IsEnabled,
            ShowFrom = snapshot.ShowFrom,
            ShowUntil = snapshot.ShowUntil,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = currentUser.Email
        });
        await context.SaveChangesAsync();
    }

    public async Task<List<HomeBannerVersionDto>> GetVersionsAsync(int homeBannerId) =>
        await context.HomeBannerVersions.AsNoTracking()
            .Where(v => v.HomeBannerId == homeBannerId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => ToVersionDto(v))
            .ToListAsync();

    public async Task<HomeBannerVersionDto?> GetVersionByIdAsync(int versionId) =>
        await context.HomeBannerVersions.AsNoTracking().Where(v => v.Id == versionId).Select(v => ToVersionDto(v)).FirstOrDefaultAsync();

    public Task ExecuteInTransactionAsync(Func<Task> work) => context.ExecuteInTransactionAsync(work);

    private static HomeBannerDto ToDto(HomeBanner b) => new()
    {
        Id = b.Id,
        ContentId = b.ContentId,
        Heading = b.Heading,
        Body = b.Body,
        IsEnabled = b.IsEnabled,
        ShowFrom = b.ShowFrom,
        ShowUntil = b.ShowUntil,
        SortOrder = b.SortOrder,
        CreatedAt = b.CreatedAt,
        CreatedBy = b.CreatedBy,
        UpdatedAt = b.UpdatedAt,
        UpdatedBy = b.UpdatedBy
    };

    private static HomeBannerVersionDto ToVersionDto(HomeBannerVersion v) => new()
    {
        Id = v.Id,
        HomeBannerId = v.HomeBannerId,
        VersionNumber = v.VersionNumber,
        Heading = v.Heading,
        Body = v.Body,
        IsEnabled = v.IsEnabled,
        ShowFrom = v.ShowFrom,
        ShowUntil = v.ShowUntil,
        CreatedAt = v.CreatedAt,
        CreatedBy = v.CreatedBy
    };
}
