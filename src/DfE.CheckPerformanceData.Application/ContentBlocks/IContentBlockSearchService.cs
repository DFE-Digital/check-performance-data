namespace DfE.CheckPerformanceData.Application.ContentBlocks;

public interface IContentBlockSearchService
{
    /// <summary>
    /// Finds content blocks whose text matches the query, mapped to the page/section that
    /// renders them. Returns at most one result per page/section URL. Empty for queries
    /// shorter than 2 characters.
    /// </summary>
    Task<List<ContentBlockSearchResultDto>> SearchAsync(string? query, int max = 20);
}
