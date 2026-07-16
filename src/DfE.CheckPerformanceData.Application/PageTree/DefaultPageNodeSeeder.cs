namespace DfE.CheckPerformanceData.Application.PageTree;

// Ensures the default root nodes exist as **content** pages, so an editor can place an actual
// landing page on /wiki, /help, /support, /guidance rather than a bare folder index. Idempotent:
// creates only the ones missing (by path); upgrades any existing root that is still typed as
// "folder" from a previous seed run (retype in place + seed an empty draft) so live environments
// pick up the new default without a database wipe. Never touches children.
//
// Roots are seeded with the hardcoded Guids from DefaultPageNodeRoots.All so their identity
// matches across every environment — a content-staging bundle from env A updates the same rows
// in env B via a GUID match rather than the path-fallback branch, and there is no risk that
// two unrelated pages authored under the same segment on different environments end up
// aliasing on import.
public sealed class DefaultPageNodeSeeder(
    IPageNodeService pageNodes,
    IPageNodeRepository pageNodeRepository)
{
    private const string EmptyContentTree = "[]";

    public async Task SeedAsync()
    {
        var sortOrder = 0;
        foreach (var (id, segment, title) in DefaultPageNodeRoots.All)
        {
            var existing = await pageNodes.GetNodeByPathAsync(segment);
            if (existing is null)
            {
                // Explicit Id via the staging-create path so the seeded row uses the hardcoded
                // Guid rather than a fresh one. Sort order tracks the list position so the tree
                // shows Support / Wiki / Help / Guidance in the declared order.
                await pageNodeRepository.CreateNodeForStagingAsync(
                    id, parentId: null, segment, path: segment,
                    title, subtitle: null, pageName: null,
                    pageType: "content", sortOrder,
                    appearInSearch: true, keywords: null, userId: "system");
            }
            else if (existing.PageType == "folder")
            {
                // Legacy folder root — upgrade in place so its children are preserved.
                await pageNodeRepository.SetPageTypeAsync(existing.Id, "content", "system");
                var versions = await pageNodeRepository.GetVersionsAsync(existing.Id);
                if (versions.Count == 0)
                    await pageNodeRepository.AddVersionAsync(
                        existing.Id, EmptyContentTree, string.Empty, null, null, "system");
            }
            sortOrder++;
        }

        // Default 404 page under /help. Renders whenever the catch-all resolver can't find a
        // page, so an editor can customise the message from the CMS rather than shipping a
        // hard-coded template. Also pinned to a stable Guid.
        var help = await pageNodes.GetNodeByPathAsync("help");
        if (help is not null && await pageNodes.GetNodeByPathAsync("help/not-found") is null)
        {
            await pageNodeRepository.CreateNodeForStagingAsync(
                DefaultPageNodeRoots.HelpNotFoundId,
                parentId: help.Id, segment: "not-found", path: "help/not-found",
                title: "Page not found", subtitle: null, pageName: null,
                pageType: "content", sortOrder: 0,
                appearInSearch: true, keywords: null, userId: "system");

            // Seed with a starter rich-text widget so authors have something to customise
            // rather than a blank canvas.
            const string starter =
                "[{\"kind\":\"widget\",\"type\":\"richtext\"," +
                "\"props\":{\"html\":\"<p class='govuk-body'>Sorry, the page you were looking for cannot be found. " +
                "It may have been moved or removed. Use the search box or the links above to find what you need.</p>\"}}]";
            await pageNodes.SaveWorkingContentAsync(DefaultPageNodeRoots.HelpNotFoundId, starter, "Page not found", "system");
            await pageNodes.PublishDraftAsync(DefaultPageNodeRoots.HelpNotFoundId, "system");
        }
    }
}
