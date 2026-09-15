using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.PageTree;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.PageTree;

// The fixture seeder does two things the sample seeder does not: it owns the root its content
// hangs off (nothing else creates it, because the root has no business existing in an environment
// nobody runs browser tests against), and it imports in Replace rather than Skip. Both are load
// bearing, so both are pinned here. What the bundle contains is covered by TestFixtureSeedBundleTests.
public class TestFixturePageNodeSeederTests
{
    private static (TestFixturePageNodeSeeder Seeder, IContentStagingService Staging,
        IPageNodeService Pages, IPageNodeRepository Repository) Build(
        PageNodeDto? existingRoot = null,
        ContentImportResult? result = null)
    {
        var staging = Substitute.For<IContentStagingService>();
        staging.ImportAsync(
                Arg.Any<ContentBundle>(),
                Arg.Any<ContentImportMode>(),
                Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>?>(),
                Arg.Any<ContentImportMode>())
            .Returns(result ?? new ContentImportResult { PageNodesCreated = 3 });

        var pages = Substitute.For<IPageNodeService>();
        pages.GetNodeByPathAsync(DefaultPageNodeRoots.DevelopmentTestingSegment).Returns(existingRoot);

        var repository = Substitute.For<IPageNodeRepository>();
        repository.CreateNodeForStagingAsync(
                Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<int>(),
                Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new PageNodeDto
            {
                Id = DefaultPageNodeRoots.DevelopmentTestingRootId,
                Segment = DefaultPageNodeRoots.DevelopmentTestingSegment,
                Path = DefaultPageNodeRoots.DevelopmentTestingSegment,
                Title = "Development testing",
                PageType = "folder",
            });

        return (new TestFixturePageNodeSeeder(pages, repository, staging), staging, pages, repository);
    }

    // Replace on collision is the fix for the failure this seeder exists to solve. A page whose
    // versions an editor deleted still exists, so a Skip-on-collision import walks straight past it
    // and the route keeps returning 404 however many times the seed is run. Replacing the versions
    // is what actually restores the page.
    [Fact]
    public async Task ImportsInReplaceMode_SoAnEmptiedFixtureIsRestored()
    {
        var (seeder, staging, _, _) = Build();

        await seeder.SeedAsync();

        await staging.Received(1).ImportAsync(
            Arg.Any<ContentBundle>(),
            ContentImportMode.Replace,
            Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>?>(),
            ContentImportMode.Replace);
    }

    [Fact]
    public async Task PassesTheFixtureBundle_ToTheImporter()
    {
        var (seeder, staging, _, _) = Build();

        await seeder.SeedAsync();

        await staging.Received(1).ImportAsync(
            Arg.Is<ContentBundle>(b =>
                b.SchemaVersion == ContentBundle.CurrentSchemaVersion && b.PageNodes.Count > 0),
            Arg.Any<ContentImportMode>(),
            Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>?>(),
            Arg.Any<ContentImportMode>());
    }

    // The root is created with the pinned Guid the bundle's pages name as their parent. Created
    // with a fresh Guid it would still appear at the right path, but every fixture would be an
    // orphan and the import would seed nothing.
    [Fact]
    public async Task CreatesTheRoot_WithThePinnedGuidTheBundleParentsTo()
    {
        var (seeder, _, _, repository) = Build(existingRoot: null);

        await seeder.SeedAsync();

        await repository.Received(1).CreateNodeForStagingAsync(
            DefaultPageNodeRoots.DevelopmentTestingRootId,
            null,
            DefaultPageNodeRoots.DevelopmentTestingSegment,
            DefaultPageNodeRoots.DevelopmentTestingSegment,
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(),
            "folder",
            Arg.Any<int>(),
            false,
            Arg.Any<string?>(), Arg.Any<string?>());
    }

    // Folder-typed and out of the menu: the root is a container, not a page. Typing it as a folder
    // keeps it out of search results by the same route every other folder is (the search query
    // filters folders structurally), and clearing ShowInMenu keeps it out of the site navigation —
    // between them, an ordinary visitor has no way to arrive at the fixture tree.
    [Fact]
    public async Task HidesTheRoot_FromTheSiteMenu()
    {
        var (seeder, _, _, repository) = Build(existingRoot: null);

        await seeder.SeedAsync();

        await repository.Received(1).SetShowInMenuAsync(
            DefaultPageNodeRoots.DevelopmentTestingRootId, false, Arg.Any<string?>());
    }

    // Seeding twice must not create a second root. The path lookup is the guard, and it has to
    // happen before the create rather than relying on the importer, because the root is not in
    // the bundle.
    [Fact]
    public async Task DoesNotRecreateTheRoot_WhenItAlreadyExists()
    {
        var existing = new PageNodeDto
        {
            Id = DefaultPageNodeRoots.DevelopmentTestingRootId,
            Segment = DefaultPageNodeRoots.DevelopmentTestingSegment,
            Path = DefaultPageNodeRoots.DevelopmentTestingSegment,
            Title = "Development testing",
            PageType = "folder",
            ShowInMenu = false,
        };
        var (seeder, _, _, repository) = Build(existingRoot: existing);

        await seeder.SeedAsync();

        await repository.DidNotReceive().CreateNodeForStagingAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<string?>());
    }

    // An environment seeded before the root was hidden — or one where somebody ticked the box in
    // the CMS — gets repaired rather than left showing test scaffolding in the site menu.
    [Fact]
    public async Task ReHidesTheRoot_WhenAnExistingOneIsVisibleInTheMenu()
    {
        var existing = new PageNodeDto
        {
            Id = DefaultPageNodeRoots.DevelopmentTestingRootId,
            Segment = DefaultPageNodeRoots.DevelopmentTestingSegment,
            Path = DefaultPageNodeRoots.DevelopmentTestingSegment,
            Title = "Development testing",
            PageType = "folder",
            ShowInMenu = true,
        };
        var (seeder, _, _, repository) = Build(existingRoot: existing);

        await seeder.SeedAsync();

        await repository.Received(1).SetShowInMenuAsync(existing.Id, false, Arg.Any<string?>());
    }

    // The admin screen reports what the press of the button did. Unlike the sample seed — where
    // "created" is the whole story because existing pages are deliberately left alone — a fixture
    // seed that repaired three emptied pages created nothing and did all the work, so both counts
    // have to be reported or the operator is told nothing happened.
    [Fact]
    public async Task ReportsCreatedAndUpdatedPages()
    {
        var (seeder, _, _, _) = Build(
            result: new ContentImportResult { PageNodesCreated = 1, PageNodesUpdated = 2 });

        var touched = await seeder.SeedAsync();

        Assert.Equal(3, touched);
    }
}
