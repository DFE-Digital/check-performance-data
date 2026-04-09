namespace DfE.CheckPerformanceData.Application.Wiki;

public interface IWikiService
{
    Task<List<WikiPageTreeNodeDto>> GetNavigationTreeAsync();
    Task<WikiPageDto?> GetPageBySlugPathAsync(string slugPath);
    Task<WikiPageDto?> GetPageByIdAsync(int id);
    Task<WikiPageDto> CreatePageAsync(CreateWikiPageDto dto);
    Task<WikiPageDto> UpdatePageAsync(int id, UpdateWikiPageDto dto);
    Task DeletePageAsync(int id);
    Task MovePageAsync(int id, int? newParentId, int newSortOrder);
    Task<List<WikiPageVersionDto>> GetPageVersionsAsync(int pageId);
    Task<WikiPageDto> RevertToVersionAsync(int pageId, int versionId);
}
