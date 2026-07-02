using DfE.CheckPerformanceData.Application.PageTree;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.PageTree;

public class SamplePageNodeSeederTests
{
    private static PageNodeDto Root(string segment) => new()
    {
        Id       = Guid.NewGuid(),
        Segment  = segment,
        Path     = segment,
        Title    = segment,
        PageType = "content"
    };

    private static IPageNodeService WithRoots(params PageNodeDto[] roots)
    {
        var svc = Substitute.For<IPageNodeService>();

        // Root lookups return the supplied stubs.
        foreach (var r in roots)
        {
            var captured = r;
            svc.GetNodeByPathAsync(captured.Path).Returns(captured);
        }

        // Any other path — the child sample lookups — returns null so the seeder creates them.
        svc.GetNodeByPathAsync(Arg.Is<string>(p => !roots.Any(r => r.Path == p)))
           .Returns((PageNodeDto?)null);

        // Create returns a stub with a fresh id.
        svc.CreatePageAsync(default, default!, default!, default!, default)
           .ReturnsForAnyArgs(ci => new PageNodeDto
           {
               Id       = Guid.NewGuid(),
               Segment  = (string)ci[1]!,
               Path     = "x",
               Title    = (string)ci[2]!,
               PageType = (string)ci[3]!,
               ParentId = (Guid?)ci[0]
           });

        return svc;
    }

    [Fact]
    public async Task WhenAllRootsPresent_SeedsThreePagesUnderEachRoot()
    {
        var wiki     = Root("wiki");
        var help     = Root("help");
        var support  = Root("support");
        var guidance = Root("guidance");
        var svc = WithRoots(wiki, help, support, guidance);

        var created = await new SamplePageNodeSeeder(svc).SeedAsync();

        Assert.Equal(12, created);   // 3 per root × 4 roots
        await svc.Received(3).CreatePageAsync(wiki.Id,     Arg.Any<string>(), Arg.Any<string>(), "content", "system");
        await svc.Received(3).CreatePageAsync(help.Id,     Arg.Any<string>(), Arg.Any<string>(), "content", "system");
        await svc.Received(3).CreatePageAsync(support.Id,  Arg.Any<string>(), Arg.Any<string>(), "content", "system");
        await svc.Received(3).CreatePageAsync(guidance.Id, Arg.Any<string>(), Arg.Any<string>(), "content", "system");
    }

    [Fact]
    public async Task EachCreatedPageIsPublishedWithAWorkingContentSave()
    {
        var svc = WithRoots(Root("wiki"), Root("help"), Root("support"), Root("guidance"));

        await new SamplePageNodeSeeder(svc).SeedAsync();

        // Every created page has both a working-content save and a publish call.
        await svc.Received(12).SaveWorkingContentAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), "system");
        await svc.Received(12).PublishDraftAsync(Arg.Any<Guid>(), "system");
    }

    [Fact]
    public async Task WhenRootMissing_SkipsThatRootsSamples()
    {
        // Only /wiki exists — the other three roots are missing.
        var svc = WithRoots(Root("wiki"));

        var created = await new SamplePageNodeSeeder(svc).SeedAsync();

        Assert.Equal(3, created);
        await svc.Received(3).CreatePageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), "content", "system");
    }

    [Fact]
    public async Task IsIdempotent_SkipsSamplesWhosePathAlreadyExists()
    {
        // /wiki/dsi-roles already exists. The seeder should skip that one and create the other two.
        var svc = Substitute.For<IPageNodeService>();
        var wiki = Root("wiki");
        var existing = new PageNodeDto
        {
            Id = Guid.NewGuid(), Segment = "dsi-roles", Path = "wiki/dsi-roles",
            Title = "Existing", PageType = "content", ParentId = wiki.Id
        };
        svc.GetNodeByPathAsync("wiki").Returns(wiki);
        svc.GetNodeByPathAsync("wiki/dsi-roles").Returns(existing);
        svc.GetNodeByPathAsync(Arg.Is<string>(p => p != "wiki" && p != "wiki/dsi-roles")).Returns((PageNodeDto?)null);
        svc.CreatePageAsync(default, default!, default!, default!, default)
           .ReturnsForAnyArgs(ci => new PageNodeDto
           {
               Id = Guid.NewGuid(), Segment = (string)ci[1]!, Path = "x",
               Title = (string)ci[2]!, PageType = (string)ci[3]!, ParentId = (Guid?)ci[0]
           });

        var created = await new SamplePageNodeSeeder(svc).SeedAsync();

        Assert.Equal(2, created);   // 3 samples under /wiki minus the one that already exists
        await svc.DidNotReceive().CreatePageAsync(wiki.Id, "dsi-roles", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ReRunOnFullyPopulatedTree_CreatesZero()
    {
        var svc = Substitute.For<IPageNodeService>();
        svc.GetNodeByPathAsync(Arg.Any<string>()).Returns(ci => new PageNodeDto
        {
            Id = Guid.NewGuid(), Segment = "x", Path = (string)ci[0]!,
            Title = "x", PageType = "content"
        });

        var created = await new SamplePageNodeSeeder(svc).SeedAsync();

        Assert.Equal(0, created);
        await svc.DidNotReceiveWithAnyArgs().CreatePageAsync(default, default!, default!, default!, default);
    }
}
