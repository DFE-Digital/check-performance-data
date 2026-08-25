using DfE.CheckPerformanceData.Application.ContentStaging;

namespace DfE.CheckPerformanceData.Application.PageTree;

// Additive, idempotent seed of the sample pages that ship with the application, hanging off the
// four default root nodes (/wiki, /help, /support, /guidance). Returns the number of pages
// actually created so the admin screen can report it.
//
// The content itself lives in an embedded content-staging bundle (see SampleContentSeedBundle)
// and is replayed through the ordinary import path, so seeded pages are created by exactly the
// code that handles a bundle uploaded by an administrator. Nothing here knows how a page is
// built — which is the point: the previous version assembled widget-tree JSON itself, and that
// hand-rolled shape could drift from what the editor actually produces.
//
// Skip on collision is what makes the seed button safe to press twice: anything already sitting
// at one of the sample identities — a developer's edits, or real content in an environment that
// happens to share a path — is left exactly as it is. Missing items are created.
public sealed class SamplePageNodeSeeder(IContentStagingService staging)
{
    public async Task<int> SeedAsync()
    {
        var result = await staging.ImportAsync(
            SampleContentSeedBundle.Load(),
            mode: ContentImportMode.Skip,
            decisions: null,
            newItemMode: ContentImportMode.Replace);

        return result.PageNodesCreated;
    }
}
