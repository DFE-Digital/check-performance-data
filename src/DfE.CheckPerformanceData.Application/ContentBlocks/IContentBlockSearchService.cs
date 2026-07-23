namespace DfE.CheckPerformanceData.Application.ContentBlocks;

public interface IContentBlockSearchService
{
    /// <summary>
    /// Finds content blocks whose text matches the query, mapped to the page/section that
    /// renders them. Returns at most one kept result per page/section URL. Empty for queries
    /// shorter than 2 characters.
    ///
    /// Returns a <see cref="ContentBlockSearchOutcome"/> — kept hits (what the user sees)
    /// alongside per-row exclusion breadcrumbs for rows the tsquery matched but a silent
    /// filter dropped. Callers consuming user-facing results read <c>outcome.Hits</c>;
    /// telemetry consumes both.
    /// </summary>
    Task<ContentBlockSearchOutcome> SearchAsync(string? query, int max = 20);
}
