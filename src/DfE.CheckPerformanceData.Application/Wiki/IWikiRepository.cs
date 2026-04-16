namespace DfE.CheckPerformanceData.Application.Wiki;

public interface IWikiRepository
{
    // Queries
    Task<List<WikiPageDto>> GetAllOrderedAsync();
    Task<WikiPageDto?> GetByIdAsync(int id);
    Task<WikiPageDto?> GetByIdIgnoringFiltersAsync(int id);
    Task<WikiPageDto?> GetBySlugAndParentAsync(string slug, int? parentId);
    Task<bool> SlugExistsAsync(string slug, int? parentId);
    Task<int> GetMaxSortOrderAsync(int? parentId);
    Task<List<WikiPageDto>> GetChildrenIgnoringFiltersAsync(int parentId);
    Task<List<WikiPageDto>> GetSiblingsAsync(int? parentId, int excludeId);
    Task<int> GetMaxVersionNumberAsync(int wikiPageId);
    Task<WikiPageVersionDto?> GetVersionByIdAsync(int versionId);
    Task<List<WikiPageVersionDto>> GetVersionsByPageIdAsync(int pageId);

    // Commands
    Task<WikiPageDto> AddPageAsync(CreateWikiPageDto dto, string slug, int sortOrder);
    Task AddVersionAsync(int wikiPageId, string title, string? content, int versionNumber);
    Task UpdatePageAsync(int id, string title, string? content, string slug);
    Task SoftDeleteRecursiveAsync(int id);
    Task MovePageAsync(int id, int? newParentId, int newSortOrder);
    Task ReorderSiblingsAsync(int? parentId, int excludeId, int insertAtPosition);
    Task ReorderSiblingsSequentialAsync(int? parentId, int excludeId);

    Task SaveChangesAsync();
}
