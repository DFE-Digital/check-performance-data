using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Web.Services;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Guidance;

// CopyAsync creates (or refreshes) two content-type page nodes under the existing guidance root,
// writes each page's serialised widget tree to its working draft, and publishes it. Idempotent:
// a second run reuses the existing nodes instead of creating duplicates.
public class GuidanceContentCopyServiceTests
{
    private readonly IPageNodeService _pageNodes = Substitute.For<IPageNodeService>();
    private readonly IContentBlockService _blocks = Substitute.For<IContentBlockService>();

    private GuidanceContentCopyService CreateService() => new(_pageNodes, _blocks);

    private static PageNodeDto Node(Guid id, string segment, string path, string pageType) =>
        new() { Id = id, Segment = segment, Path = path, Title = segment, PageType = pageType };

    [Fact]
    public async Task Copy_WhenGuidanceRootMissing_ReturnsMissingResult_AndCreatesNothing()
    {
        _pageNodes.GetNodeByPathAsync("guidance").Returns((PageNodeDto?)null);

        var result = await CreateService().CopyAsync("editor@example.com");

        Assert.True(result.GuidanceRootMissing);
        await _pageNodes.DidNotReceiveWithAnyArgs()
            .CreatePageAsync(default, default!, default!, default!, default);
    }

    [Fact]
    public async Task Copy_CreatesTwoContentNodesUnderGuidance_ThenSavesAndPublishesEach()
    {
        var guidanceId = Guid.NewGuid();
        var landingId = Guid.NewGuid();
        var ks4Id = Guid.NewGuid();

        _pageNodes.GetNodeByPathAsync("guidance")
            .Returns(Node(guidanceId, "guidance", "guidance", "folder"));
        _pageNodes.GetNodeByPathAsync("guidance/landing-test").Returns((PageNodeDto?)null);
        _pageNodes.GetNodeByPathAsync("guidance/ks4-test").Returns((PageNodeDto?)null);

        _pageNodes.CreatePageAsync(guidanceId, "landing-test", "Landing test", "content", "editor")
            .Returns(Node(landingId, "landing-test", "guidance/landing-test", "content"));
        _pageNodes.CreatePageAsync(guidanceId, "ks4-test", "KS4 test", "content", "editor")
            .Returns(Node(ks4Id, "ks4-test", "guidance/ks4-test", "content"));

        var result = await CreateService().CopyAsync("editor");

        Assert.False(result.GuidanceRootMissing);
        Assert.Contains("guidance/landing-test", result.Created);
        Assert.Contains("guidance/ks4-test", result.Created);

        await _pageNodes.Received(1)
            .CreatePageAsync(guidanceId, "landing-test", "Landing test", "content", "editor");
        await _pageNodes.Received(1)
            .CreatePageAsync(guidanceId, "ks4-test", "KS4 test", "content", "editor");

        await _pageNodes.Received(1).SaveWorkingContentAsync(landingId, Arg.Any<string>(), "", "editor");
        await _pageNodes.Received(1).PublishDraftAsync(landingId, "editor");
        await _pageNodes.Received(1).SaveWorkingContentAsync(ks4Id, Arg.Any<string>(), "", "editor");
        await _pageNodes.Received(1).PublishDraftAsync(ks4Id, "editor");
    }

    [Fact]
    public async Task Copy_WhenNodesAlreadyExist_ReusesThemInsteadOfCreating()
    {
        var guidanceId = Guid.NewGuid();
        var landingId = Guid.NewGuid();
        var ks4Id = Guid.NewGuid();

        _pageNodes.GetNodeByPathAsync("guidance")
            .Returns(Node(guidanceId, "guidance", "guidance", "folder"));
        _pageNodes.GetNodeByPathAsync("guidance/landing-test")
            .Returns(Node(landingId, "landing-test", "guidance/landing-test", "content"));
        _pageNodes.GetNodeByPathAsync("guidance/ks4-test")
            .Returns(Node(ks4Id, "ks4-test", "guidance/ks4-test", "content"));

        var result = await CreateService().CopyAsync("editor");

        Assert.Contains("guidance/landing-test", result.Updated);
        Assert.Contains("guidance/ks4-test", result.Updated);
        await _pageNodes.DidNotReceiveWithAnyArgs()
            .CreatePageAsync(default, default!, default!, default!, default);

        await _pageNodes.Received(1).SaveWorkingContentAsync(landingId, Arg.Any<string>(), "", "editor");
        await _pageNodes.Received(1).PublishDraftAsync(landingId, "editor");
        await _pageNodes.Received(1).SaveWorkingContentAsync(ks4Id, Arg.Any<string>(), "", "editor");
        await _pageNodes.Received(1).PublishDraftAsync(ks4Id, "editor");
    }
}
