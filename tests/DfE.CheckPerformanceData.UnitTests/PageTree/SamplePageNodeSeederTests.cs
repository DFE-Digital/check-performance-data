using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.PageTree;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.PageTree;

// The seeder replays the shipped sample-content bundle through the content-staging importer
// rather than assembling pages itself, so what is worth pinning here is the contract it hands to
// the importer — the modes decide whether re-seeding is additive or destructive — and how it
// reports back to the admin screen. What the bundle actually contains is covered by
// SampleContentSeedBundleTests.
public class SamplePageNodeSeederTests
{
    private static (SamplePageNodeSeeder Seeder, IContentStagingService Staging) Build(
        ContentImportResult? result = null)
    {
        var staging = Substitute.For<IContentStagingService>();
        staging.ImportAsync(
                Arg.Any<ContentBundle>(),
                Arg.Any<ContentImportMode>(),
                Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>?>(),
                Arg.Any<ContentImportMode>())
            .Returns(result ?? new ContentImportResult { PageNodesCreated = 13 });

        return (new SamplePageNodeSeeder(staging), staging);
    }

    // Skip on collision is what makes re-seeding safe: a developer who has edited a sample page,
    // or an environment carrying real content at the same path, must not have it overwritten by
    // pressing the seed button. Replace for new items means "create the ones that are missing".
    [Fact]
    public async Task Seeds_Additively_LeavingExistingContentAlone()
    {
        var (seeder, staging) = Build();

        await seeder.SeedAsync();

        await staging.Received(1).ImportAsync(
            Arg.Any<ContentBundle>(),
            ContentImportMode.Skip,
            Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>?>(),
            ContentImportMode.Replace);
    }

    [Fact]
    public async Task PassesTheShippedBundle_ToTheImporter()
    {
        var (seeder, staging) = Build();

        await seeder.SeedAsync();

        await staging.Received(1).ImportAsync(
            Arg.Is<ContentBundle>(b =>
                b.SchemaVersion == ContentBundle.CurrentSchemaVersion && b.PageNodes.Count > 0),
            Arg.Any<ContentImportMode>(),
            Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>?>(),
            Arg.Any<ContentImportMode>());
    }

    // The admin screen reports "Added N sample pages", so the seeder returns pages created — not
    // pages skipped, and not content blocks.
    [Fact]
    public async Task ReturnsTheNumberOfPagesCreated()
    {
        var (seeder, _) = Build(new ContentImportResult { PageNodesCreated = 4, PageNodesSkipped = 9 });

        var created = await seeder.SeedAsync();

        Assert.Equal(4, created);
    }

    // Re-running on a fully seeded environment adds nothing, which the admin screen renders as
    // "Sample pages are already present."
    [Fact]
    public async Task ReturnsZero_WhenEverySampleIsAlreadyPresent()
    {
        var (seeder, _) = Build(new ContentImportResult { PageNodesCreated = 0, PageNodesSkipped = 13 });

        var created = await seeder.SeedAsync();

        Assert.Equal(0, created);
    }
}
