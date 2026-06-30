namespace DfE.CheckPerformanceData.Application.ContentPages;

// Editing and versioning operations for content pages. See <see cref="ContentPageService"/> for the
// draft-autosave / publish-snapshots-version / restore-republishes-new-version semantics.
public interface IContentPageService
{
    Task<ContentPageDto> CreateAsync(string slug, string title, string? caption, string layout);

    Task<ContentPageDto?> GetBySlugAsync(string slug);

    Task SaveDraftAsync(string slug, string draftContent);

    Task<int> PublishAsync(string slug);

    Task<int> RestoreVersionAsync(string slug, int versionNumber);

    Task<string?> GetPublishedContentAsync(string slug);

    Task<List<ContentPageVersionDto>> GetVersionsAsync(string slug);
}
