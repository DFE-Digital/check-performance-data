using DfE.CheckPerformanceData.Application.PageTree;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.PageTree;

public class DefaultPageNodeSeederTests
{
    private static PageNodeDto StubDto(string segment, string pageType = "content", Guid? id = null) => new()
    {
        Id       = id ?? Guid.NewGuid(),
        Segment  = segment,
        Path     = segment,
        Title    = segment,
        PageType = pageType
    };

    private static (IPageNodeService Svc, IPageNodeRepository Repo) BuildDeps(
        params (string Path, string PageType)[] existingNodes)
    {
        var svc  = Substitute.For<IPageNodeService>();
        var repo = Substitute.For<IPageNodeRepository>();

        foreach (var (path, pageType) in existingNodes)
        {
            var captured = path;
            svc.GetNodeByPathAsync(captured).Returns(StubDto(captured, pageType));
        }

        // Missing paths return null.
        svc.GetNodeByPathAsync(Arg.Is<string>(p => !existingNodes.Any(e => e.Path == p)))
           .Returns((PageNodeDto?)null);

        // Repository staging-create is now the seed path — pin a return so downstream
        // seeder logic (working-content save, publish) has an id to work with.
        repo.CreateNodeForStagingAsync(
                Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>())
            .Returns(ci => StubDto((string)ci[2], "content", (Guid)ci[0]));

        // Retype-upgrade path: after SetPageType flips to content, seeder checks versions and only
        // adds a draft if none exist. Empty list = no existing draft.
        repo.GetVersionsAsync(Arg.Any<Guid>()).Returns([]);

        return (svc, repo);
    }

    [Fact]
    public async Task WhenNoneExist_CreatesAllFourRootsAsContent_WithHardcodedGuids()
    {
        var (svc, repo) = BuildDeps();
        await new DefaultPageNodeSeeder(svc, repo).SeedAsync();

        // Each root seeds with its hardcoded Guid from DefaultPageNodeRoots.All, not a
        // freshly-generated one — that's the whole point of the pinning.
        foreach (var (id, segment, title) in DefaultPageNodeRoots.All)
        {
            await repo.Received(1).CreateNodeForStagingAsync(
                id, null, segment, segment, title,
                Arg.Any<string?>(), Arg.Any<string?>(),
                "content", Arg.Any<int>(), "system");
        }
    }

    [Theory]
    [InlineData("support")]
    [InlineData("wiki")]
    [InlineData("help")]
    [InlineData("guidance")]
    public async Task WhenNoneExist_CreatesEachRootWithCorrectSegment(string segment)
    {
        var (svc, repo) = BuildDeps();
        await new DefaultPageNodeSeeder(svc, repo).SeedAsync();

        var expectedId = DefaultPageNodeRoots.All.Single(r => r.Segment == segment).Id;
        await repo.Received(1).CreateNodeForStagingAsync(
            expectedId, null, segment, segment, Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(),
            "content", Arg.Any<int>(), "system");
    }

    [Fact]
    public async Task WhenAllExistAsContent_CreatesNothing_And_DoesNotRetype()
    {
        // help/not-found is treated as already present so the seeder does not add anything.
        var (svc, repo) = BuildDeps(
            ("support",         "content"),
            ("wiki",            "content"),
            ("help",            "content"),
            ("guidance",        "content"),
            ("help/not-found",  "content"));
        await new DefaultPageNodeSeeder(svc, repo).SeedAsync();

        await repo.DidNotReceiveWithAnyArgs().CreateNodeForStagingAsync(
            default, default, default!, default!, default!, default, default, default!, default, default);
        await repo.DidNotReceiveWithAnyArgs().SetPageTypeAsync(default, default!, default);
    }

    [Fact]
    public async Task WhenSomeMissing_CreatesOnlyMissingAsContent()
    {
        var (svc, repo) = BuildDeps(
            ("help",     "content"),
            ("guidance", "content"),
            ("help/not-found", "content"));
        await new DefaultPageNodeSeeder(svc, repo).SeedAsync();

        var supportId  = DefaultPageNodeRoots.All.Single(r => r.Segment == "support").Id;
        var wikiId     = DefaultPageNodeRoots.All.Single(r => r.Segment == "wiki").Id;
        var helpId     = DefaultPageNodeRoots.All.Single(r => r.Segment == "help").Id;
        var guidanceId = DefaultPageNodeRoots.All.Single(r => r.Segment == "guidance").Id;

        await repo.Received(1).CreateNodeForStagingAsync(
            supportId, null, "support", "support", Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), "content", Arg.Any<int>(), "system");
        await repo.Received(1).CreateNodeForStagingAsync(
            wikiId, null, "wiki", "wiki", Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), "content", Arg.Any<int>(), "system");
        await repo.DidNotReceive().CreateNodeForStagingAsync(
            helpId, Arg.Any<Guid?>(), "help", Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>());
        await repo.DidNotReceive().CreateNodeForStagingAsync(
            guidanceId, Arg.Any<Guid?>(), "guidance", Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task WhenExistsAsLegacyFolder_UpgradesToContent_AndSeedsEmptyDraftIfMissing()
    {
        // /support already exists as folder from a previous seed run. Upgrade in place.
        var supportId = Guid.NewGuid();
        var svc  = Substitute.For<IPageNodeService>();
        var repo = Substitute.For<IPageNodeRepository>();

        svc.GetNodeByPathAsync("support").Returns(new PageNodeDto
        {
            Id = supportId, Segment = "support", Path = "support", Title = "Support", PageType = "folder"
        });
        svc.GetNodeByPathAsync(Arg.Is<string>(p => p != "support")).Returns((PageNodeDto?)null);
        repo.CreateNodeForStagingAsync(
                Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string?>())
            .Returns(ci => StubDto((string)ci[2], "content", (Guid)ci[0]));
        repo.GetVersionsAsync(supportId).Returns([]);

        await new DefaultPageNodeSeeder(svc, repo).SeedAsync();

        await repo.Received(1).SetPageTypeAsync(supportId, "content", "system");
        // Empty draft seeded because GetVersionsAsync returned empty.
        await repo.Received(1).AddVersionAsync(
            supportId,
            Arg.Is<string>(s => s == "[]"),
            Arg.Any<string>(),
            null,
            null,
            "system");
    }
}
