using DfE.CheckPerformanceData.Application.PageTree;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.PageTree;

public class DefaultPageNodeSeederTests
{
    private static PageNodeDto StubDto(string segment) => new()
    {
        Id       = Guid.NewGuid(),
        Segment  = segment,
        Path     = segment,
        Title    = segment,
        PageType = "folder"
    };

    private static IPageNodeService BuildService(params string[] existingPaths)
    {
        var svc = Substitute.For<IPageNodeService>();

        // Return a stub for paths that already exist.
        foreach (var path in existingPaths)
        {
            var captured = path;
            svc.GetNodeByPathAsync(captured).Returns(StubDto(captured));
        }

        // All other paths return null (not yet seeded).
        svc.GetNodeByPathAsync(Arg.Is<string>(p => !existingPaths.Contains(p)))
           .Returns((PageNodeDto?)null);

        svc.CreatePageAsync(default, default!, default!, default!, default)
           .ReturnsForAnyArgs(StubDto("x"));

        return svc;
    }

    [Fact]
    public async Task WhenNoneExist_CreatesAllFourRoots()
    {
        var svc = BuildService();
        await new DefaultPageNodeSeeder(svc).SeedAsync();

        await svc.Received(4).CreatePageAsync(
            Arg.Is<Guid?>(p => p == null),
            Arg.Any<string>(),
            Arg.Any<string>(),
            "folder",
            "system");
    }

    [Theory]
    [InlineData("support")]
    [InlineData("wiki")]
    [InlineData("help")]
    [InlineData("guidance")]
    public async Task WhenNoneExist_CreatesEachRootWithCorrectSegment(string segment)
    {
        var svc = BuildService();
        await new DefaultPageNodeSeeder(svc).SeedAsync();

        await svc.Received(1).CreatePageAsync(
            null,
            segment,
            Arg.Any<string>(),
            "folder",
            "system");
    }

    [Fact]
    public async Task WhenAllExist_CreatesNothing()
    {
        var svc = BuildService("support", "wiki", "help", "guidance");
        await new DefaultPageNodeSeeder(svc).SeedAsync();

        await svc.DidNotReceiveWithAnyArgs().CreatePageAsync(default, default!, default!, default!, default);
    }

    [Fact]
    public async Task WhenSomeExist_CreatesOnlyMissing()
    {
        // "help" and "guidance" already exist; "support" and "wiki" are missing.
        var svc = BuildService("help", "guidance");
        await new DefaultPageNodeSeeder(svc).SeedAsync();

        await svc.Received(2).CreatePageAsync(
            null, Arg.Any<string>(), Arg.Any<string>(), "folder", "system");

        await svc.Received(1).CreatePageAsync(null, "support", Arg.Any<string>(), "folder", "system");
        await svc.Received(1).CreatePageAsync(null, "wiki",    Arg.Any<string>(), "folder", "system");

        await svc.DidNotReceive().CreatePageAsync(null, "help",     Arg.Any<string>(), "folder", "system");
        await svc.DidNotReceive().CreatePageAsync(null, "guidance", Arg.Any<string>(), "folder", "system");
    }

    [Fact]
    public async Task AlwaysCreatesAsFolder_WithNullParent_AndSystemUser()
    {
        var svc = BuildService();
        await new DefaultPageNodeSeeder(svc).SeedAsync();

        var calls = svc.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IPageNodeService.CreatePageAsync))
            .ToList();

        Assert.Equal(4, calls.Count);
        foreach (var call in calls)
        {
            var args = call.GetArguments();
            Assert.Null(args[0]);            // parentId = null
            Assert.Equal("folder", args[3]); // pageType = "folder"
            Assert.Equal("system", args[4]); // userId   = "system"
        }
    }
}
