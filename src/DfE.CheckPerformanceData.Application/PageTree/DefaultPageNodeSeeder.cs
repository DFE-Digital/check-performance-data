namespace DfE.CheckPerformanceData.Application.PageTree;

// Ensures the default root nodes exist as **content** pages, so an editor can place an actual
// landing page on /wiki, /help, /support, /guidance rather than a bare folder index. Idempotent:
// creates only the ones missing (by path); upgrades any existing root that is still typed as
// "folder" from a previous seed run (retype in place + seed an empty draft) so live environments
// pick up the new default without a database wipe. Never touches children.
public sealed class DefaultPageNodeSeeder(
    IPageNodeService pageNodes,
    IPageNodeRepository pageNodeRepository)
{
    private const string EmptyContentTree = "[]";

    public async Task SeedAsync()
    {
        foreach (var (segment, title) in DefaultPageNodeRoots.All)
        {
            var existing = await pageNodes.GetNodeByPathAsync(segment);
            if (existing is null)
            {
                await pageNodes.CreatePageAsync(null, segment, title, "content", "system");
                continue;
            }

            if (existing.PageType == "folder")
            {
                // Legacy folder root — upgrade in place so its children are preserved.
                await pageNodeRepository.SetPageTypeAsync(existing.Id, "content", "system");
                var versions = await pageNodeRepository.GetVersionsAsync(existing.Id);
                if (versions.Count == 0)
                    await pageNodeRepository.AddVersionAsync(
                        existing.Id, EmptyContentTree, string.Empty, null, null, "system");
            }
        }
    }
}
