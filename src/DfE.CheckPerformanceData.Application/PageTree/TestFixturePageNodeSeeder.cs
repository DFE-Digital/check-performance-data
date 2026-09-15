using DfE.CheckPerformanceData.Application.ContentStaging;

namespace DfE.CheckPerformanceData.Application.PageTree;

// Seeds the content the automated browser tests navigate to, under a root of its own.
//
// Two things separate this from SamplePageNodeSeeder, and both are the point of it existing:
//
//   * It owns its root. Nothing creates /development-testing at start-up, because a container for
//     test scaffolding has no business appearing in an environment nobody runs the browser suite
//     against. Created here, the root shows up only where somebody has actually seeded fixtures.
//
//   * It imports in Replace, not Skip. The suite was failing because sample pages it navigated to
//     had been emptied — an editor deleted the version, the page row survived, and the route
//     started returning 404. Skip-on-collision cannot repair that: the collision test is the page
//     identity, not whether the page has anything to render, so the seed walks past the broken row
//     every time. Replacing the versions is what brings the page back.
//
// Replace is safe here in a way it would not be for the sample content: nothing under this root is
// anyone's work. It is re-imported wholesale on every seed by design, so a fixture is always in the
// state the tests expect rather than whatever the last person to open it left behind.
public sealed class TestFixturePageNodeSeeder(
    IPageNodeService pageNodes,
    IPageNodeRepository pageNodeRepository,
    IContentStagingService staging)
{
    private const string RootTitle = "Development testing";

    private const string RootSubtitle =
        "Content the automated tests navigate to. Re-created on every seed — edits here are lost.";

    /// <summary>
    /// Creates the fixture root if it is missing, then imports the fixture bundle over the top of
    /// whatever is there. Returns the number of fixture pages created or repaired, which is what
    /// the admin screen reports back.
    /// </summary>
    public async Task<int> SeedAsync()
    {
        await EnsureRootAsync();

        var result = await staging.ImportAsync(
            TestFixtureSeedBundle.Load(),
            mode: ContentImportMode.Replace,
            decisions: null,
            newItemMode: ContentImportMode.Replace);

        // Both counts, unlike the sample seed. There, "created" is the whole story because existing
        // pages are deliberately left alone. Here a run that repaired three emptied fixtures created
        // nothing at all and did all of the work, so reporting creations alone would tell the
        // operator that nothing happened.
        return result.PageNodesCreated + result.PageNodesUpdated;
    }

    // Folder-typed and out of the menu. Folder is what keeps the root out of search results — the
    // search query filters folders structurally, so it needs no flag of its own — and clearing
    // ShowInMenu keeps it out of the site navigation. Between them there is no route by which an
    // ordinary visitor arrives at the fixture tree, while an editor browsing the page tree sees one
    // plainly-labelled container rather than test pages interleaved with their own.
    //
    // The re-hide on an existing root is not belt-and-braces: ShowInMenu does not round-trip
    // through a content-staging bundle, so a root that travelled between environments inside an
    // export arrives with the column at its default of true.
    private async Task EnsureRootAsync()
    {
        var existing = await pageNodes.GetNodeByPathAsync(DefaultPageNodeRoots.DevelopmentTestingSegment);

        if (existing is null)
        {
            // Explicit Id through the staging-create path so the row carries the pinned Guid the
            // bundle's pages name as their parent. Created with a fresh one the root would still
            // sit at the right path, but every fixture would be an orphan and the import would
            // quietly seed nothing.
            var created = await pageNodeRepository.CreateNodeForStagingAsync(
                DefaultPageNodeRoots.DevelopmentTestingRootId,
                parentId: null,
                DefaultPageNodeRoots.DevelopmentTestingSegment,
                path: DefaultPageNodeRoots.DevelopmentTestingSegment,
                RootTitle, RootSubtitle, pageName: null,
                pageType: "folder", sortOrder: 100,
                appearInSearch: false, keywords: null, userId: "system");

            await pageNodeRepository.SetShowInMenuAsync(created.Id, false, "system");
            return;
        }

        if (existing.ShowInMenu)
            await pageNodeRepository.SetShowInMenuAsync(existing.Id, false, "system");
    }
}
