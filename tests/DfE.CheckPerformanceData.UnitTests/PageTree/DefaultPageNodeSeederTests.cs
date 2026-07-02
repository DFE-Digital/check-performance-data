using DfE.CheckPerformanceData.Application.PageTree;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.PageTree;

public class DefaultPageNodeSeederTests
{
    private static PageNodeDto StubDto(string segment, string pageType = "content") => new()
    {
        Id       = Guid.NewGuid(),
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

        svc.CreatePageAsync(default, default!, default!, default!, default)
           .ReturnsForAnyArgs(StubDto("x"));

        // Retype-upgrade path: after SetPageType flips to content, seeder checks versions and only
        // adds a draft if none exist. Empty list = no existing draft.
        repo.GetVersionsAsync(Arg.Any<Guid>()).Returns([]);

        return (svc, repo);
    }

    [Fact]
    public async Task WhenNoneExist_CreatesAllFourRootsAsContent()
    {
        var (svc, repo) = BuildDeps();
        await new DefaultPageNodeSeeder(svc, repo).SeedAsync();

        await svc.Received(4).CreatePageAsync(
            Arg.Is<Guid?>(p => p == null),
            Arg.Any<string>(),
            Arg.Any<string>(),
            "content",
            "system");
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

        await svc.Received(1).CreatePageAsync(
            null,
            segment,
            Arg.Any<string>(),
            "content",
            "system");
    }

    [Fact]
    public async Task WhenAllExistAsContent_CreatesNothing_And_DoesNotRetype()
    {
        var (svc, repo) = BuildDeps(
            ("support",  "content"),
            ("wiki",     "content"),
            ("help",     "content"),
            ("guidance", "content"));
        await new DefaultPageNodeSeeder(svc, repo).SeedAsync();

        await svc.DidNotReceiveWithAnyArgs().CreatePageAsync(default, default!, default!, default!, default);
        await repo.DidNotReceiveWithAnyArgs().SetPageTypeAsync(default, default!, default);
    }

    [Fact]
    public async Task WhenSomeMissing_CreatesOnlyMissingAsContent()
    {
        var (svc, repo) = BuildDeps(
            ("help",     "content"),
            ("guidance", "content"));
        await new DefaultPageNodeSeeder(svc, repo).SeedAsync();

        await svc.Received(1).CreatePageAsync(null, "support", Arg.Any<string>(), "content", "system");
        await svc.Received(1).CreatePageAsync(null, "wiki",    Arg.Any<string>(), "content", "system");
        await svc.DidNotReceive().CreatePageAsync(null, "help",     Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        await svc.DidNotReceive().CreatePageAsync(null, "guidance", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
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
        svc.CreatePageAsync(default, default!, default!, default!, default).ReturnsForAnyArgs(StubDto("x"));
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
