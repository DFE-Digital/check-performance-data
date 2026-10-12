namespace DfE.CheckPerformanceData.Application.HomeBanners;

public interface IHomeBannerRepository
{
    /// <summary>Every banner, ordered by SortOrder then Id. Status is NOT set; the service does that.</summary>
    Task<List<HomeBannerDto>> GetAllAsync();
    Task<HomeBannerDto?> GetByIdAsync(int id);
    Task<HomeBannerDto?> GetByContentIdAsync(Guid contentId);

    /// <summary>Adds a banner. Null sortOrder = at the end of the order; null contentId = database-generated.</summary>
    Task<HomeBannerDto> AddAsync(HomeBannerContent content, Guid? contentId = null, int? sortOrder = null);
    Task UpdateAsync(int id, HomeBannerContent content);
    Task DeleteAsync(int id);
    Task SetSortOrdersAsync(IReadOnlyList<(int Id, int SortOrder)> orders);

    Task<int> GetMaxVersionNumberAsync(int homeBannerId);
    Task AddVersionAsync(int homeBannerId, HomeBannerContent snapshot, int versionNumber);
    /// <summary>Newest first.</summary>
    Task<List<HomeBannerVersionDto>> GetVersionsAsync(int homeBannerId);
    Task<HomeBannerVersionDto?> GetVersionByIdAsync(int versionId);

    Task ExecuteInTransactionAsync(Func<Task> work);
}
