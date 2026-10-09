using DfE.CheckPerformanceData.Application.Common;

namespace DfE.CheckPerformanceData.Application.HomeBanners;

public sealed class HomeBannerService(
    IHomeBannerRepository repository,
    IHtmlRenderingService htmlRenderer,
    TimeProvider timeProvider) : IHomeBannerService
{
    // UK wall-clock "now" (#535): the same clock every checking-exercise gate reads.
    private DateTime Now() => timeProvider.GetLocalNow().DateTime;

    public async Task<IReadOnlyList<HomeBannerDto>> GetLiveAsync()
    {
        var now = Now();
        return (await repository.GetAllAsync())
            .Where(b => HomeBannerRules.IsLive(b.IsEnabled, b.ShowFrom, b.ShowUntil, now))
            .Select(b => Enrich(b, now))
            .ToList();
    }

    public async Task<IReadOnlyList<HomeBannerDto>> GetAllAsync()
    {
        var now = Now();
        return (await repository.GetAllAsync()).Select(b => Enrich(b, now)).ToList();
    }

    public async Task<HomeBannerDto?> GetByIdAsync(int id)
    {
        var banner = await repository.GetByIdAsync(id);
        return banner is null ? null : Enrich(banner, Now());
    }

    public async Task<HomeBannerDto> CreateAsync(HomeBannerContent content)
    {
        var clean = Sanitise(content);
        HomeBannerDto? created = null;
        await repository.ExecuteInTransactionAsync(async () =>
        {
            created = await repository.AddAsync(clean);
            await repository.AddVersionAsync(created.Id, clean, 1);
        });
        return Enrich(created!, Now());
    }

    public async Task<HomeBannerDto?> UpdateAsync(int id, HomeBannerContent content)
    {
        if (await repository.GetByIdAsync(id) is null) return null;
        await WriteAsync(id, Sanitise(content));
        return await GetByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        if (await repository.GetByIdAsync(id) is null) return false;
        await repository.DeleteAsync(id);
        return true;
    }

    public async Task MoveAsync(int id, string direction)
    {
        // Same approach as PageNodeService.MoveAsync: reorder the list, then renumber from zero so
        // the operation is idempotent even when every SortOrder is equal (as after an import).
        var all = (await repository.GetAllAsync()).OrderBy(b => b.SortOrder).ThenBy(b => b.Id).ToList();
        var idx = all.FindIndex(b => b.Id == id);
        if (idx < 0) return;
        var target = direction == "up" ? idx - 1 : idx + 1;
        if (target < 0 || target >= all.Count) return;

        var item = all[idx];
        all.RemoveAt(idx);
        all.Insert(target, item);
        await repository.SetSortOrdersAsync(all.Select((b, i) => (b.Id, i)).ToList());
    }

    public async Task<IReadOnlyList<HomeBannerVersionDto>> GetVersionsAsync(int id) =>
        (await repository.GetVersionsAsync(id))
            .Select(v => new HomeBannerVersionDto
            {
                Id = v.Id,
                HomeBannerId = v.HomeBannerId,
                VersionNumber = v.VersionNumber,
                Heading = v.Heading,
                Body = v.Body,
                BodyHtml = htmlRenderer.RenderHtml(v.Body),
                IsEnabled = v.IsEnabled,
                ShowFrom = v.ShowFrom,
                ShowUntil = v.ShowUntil,
                CreatedAt = v.CreatedAt,
                CreatedBy = v.CreatedBy
            })
            .ToList();

    public async Task<HomeBannerDto?> RestoreVersionAsync(int id, int versionId)
    {
        if (await repository.GetByIdAsync(id) is null) return null;
        var version = await repository.GetVersionByIdAsync(versionId);
        if (version is null || version.HomeBannerId != id) return null;

        // The version was sanitised when it was written; no need to sanitise again.
        await WriteAsync(id, version.ToContent());
        return await GetByIdAsync(id);
    }

    private async Task WriteAsync(int id, HomeBannerContent content)
    {
        await repository.ExecuteInTransactionAsync(async () =>
        {
            var max = await repository.GetMaxVersionNumberAsync(id);
            await repository.UpdateAsync(id, content);
            await repository.AddVersionAsync(id, content, max + 1);
        });
    }

    private HomeBannerContent Sanitise(HomeBannerContent content) => content with
    {
        Heading = content.Heading.Trim(),
        Body = htmlRenderer.RenderHtml(content.Body) ?? string.Empty
    };

    private HomeBannerDto Enrich(HomeBannerDto b, DateTime now) => new()
    {
        Id = b.Id,
        ContentId = b.ContentId,
        Heading = b.Heading,
        Body = b.Body,
        BodyHtml = htmlRenderer.RenderHtml(b.Body),
        IsEnabled = b.IsEnabled,
        ShowFrom = b.ShowFrom,
        ShowUntil = b.ShowUntil,
        SortOrder = b.SortOrder,
        Status = HomeBannerRules.StatusOf(b.IsEnabled, b.ShowFrom, b.ShowUntil, now),
        CreatedAt = b.CreatedAt,
        CreatedBy = b.CreatedBy,
        UpdatedAt = b.UpdatedAt,
        UpdatedBy = b.UpdatedBy
    };
}
