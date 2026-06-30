namespace DfE.CheckPerformanceData.Application.ContentPages;

// Orchestrates content-page editing and versioning. The draft tree is always editable and autosaves
// with no version churn; publishing snapshots the draft as the next integer version and marks it the
// published one; restoring re-publishes an old snapshot as a brand-new version so history only grows.
public sealed class ContentPageService(IContentPageRepository repository) : IContentPageService
{
    private const string EmptyTree = "[]";

    public Task<ContentPageDto> CreateAsync(string slug, string title, string? caption, string layout) =>
        repository.CreateAsync(slug, title, caption, layout, EmptyTree);

    public Task<List<ContentPageSummaryDto>> GetAllAsync() =>
        repository.GetAllAsync();

    public Task<ContentPageDto?> GetBySlugAsync(string slug) =>
        repository.GetBySlugAsync(slug);

    public async Task SaveDraftAsync(string slug, string draftContent)
    {
        var page = await Require(slug);
        await repository.UpdateDraftAsync(page.Id, draftContent);
    }

    // Snapshots the current draft as the next version and marks it published. Returns the new number.
    public async Task<int> PublishAsync(string slug)
    {
        var page = await Require(slug);
        return await SnapshotAndPublish(page.Id, page.DraftContent, page.Title);
    }

    // Restores a past version: its snapshot becomes the live draft and is re-published as a new
    // version. Returns the new version number.
    public async Task<int> RestoreVersionAsync(string slug, int versionNumber)
    {
        var page = await Require(slug);
        var version = await repository.GetVersionAsync(page.Id, versionNumber)
            ?? throw new InvalidOperationException($"Content page '{slug}' has no version {versionNumber}.");

        return await SnapshotAndPublish(page.Id, version.Snapshot, page.Title, restoreDraft: true);
    }

    public async Task<string?> GetPublishedContentAsync(string slug)
    {
        var page = await repository.GetBySlugAsync(slug);
        if (page?.PublishedVersionNumber is not { } published) return null;

        var version = await repository.GetVersionAsync(page.Id, published);
        return version?.Snapshot;
    }

    public async Task<List<ContentPageVersionDto>> GetVersionsAsync(string slug)
    {
        var page = await Require(slug);
        return await repository.GetVersionsAsync(page.Id);
    }

    private async Task<int> SnapshotAndPublish(int pageId, string content, string title, bool restoreDraft = false)
    {
        var version = 0;
        await repository.ExecuteInTransactionAsync(async () =>
        {
            if (restoreDraft)
                await repository.UpdateDraftAsync(pageId, content);

            version = await repository.GetMaxVersionNumberAsync(pageId) + 1;
            await repository.AddVersionAsync(pageId, version, content, title);
            await repository.SetPublishedVersionAsync(pageId, version);
        });
        return version;
    }

    private async Task<ContentPageDto> Require(string slug) =>
        await repository.GetBySlugAsync(slug)
            ?? throw new InvalidOperationException($"Content page '{slug}' not found.");
}
