namespace DfE.CheckPerformanceData.Application.HomeBanners;

/// <summary>
/// Start-page banners (#566). The only caller of <see cref="HomeBannerRules"/> with the real clock:
/// controllers and views read Status and the live list from here and never compare dates.
/// </summary>
public interface IHomeBannerService
{
    /// <summary>The banners the start page shows now, in order. Empty when there are none.</summary>
    Task<IReadOnlyList<HomeBannerDto>> GetLiveAsync();
    /// <summary>Every banner in order, each with its Status.</summary>
    Task<IReadOnlyList<HomeBannerDto>> GetAllAsync();
    Task<HomeBannerDto?> GetByIdAsync(int id);
    /// <summary>Sanitises the body, creates the banner at the end of the order and records version 1.</summary>
    Task<HomeBannerDto> CreateAsync(HomeBannerContent content);
    /// <summary>Sanitises the body, overwrites the banner and records a new version. Null when the id is unknown.</summary>
    Task<HomeBannerDto?> UpdateAsync(int id, HomeBannerContent content);
    /// <summary>True when a banner was deleted; false when the id is unknown.</summary>
    Task<bool> DeleteAsync(int id);
    /// <summary>Moves the banner one place "up" or "down". No-op at either end or for an unknown id.</summary>
    Task MoveAsync(int id, string direction);
    Task<IReadOnlyList<HomeBannerVersionDto>> GetVersionsAsync(int id);
    /// <summary>Copies the version's fields onto the banner and records that as a new version. Null when either id is unknown.</summary>
    Task<HomeBannerDto?> RestoreVersionAsync(int id, int versionId);
}
