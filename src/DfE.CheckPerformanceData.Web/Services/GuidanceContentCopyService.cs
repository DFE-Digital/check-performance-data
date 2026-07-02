using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Web.Services;

// Outcome of a guidance-copy run: whether the guidance root was found, and which page paths were
// freshly created versus refreshed in place. Re-running is idempotent — the same two pages move from
// Created to Updated on the second call.
public sealed class GuidanceContentCopyResult
{
    public bool GuidanceRootMissing { get; init; }
    public List<string> Created { get; } = [];
    public List<string> Updated { get; } = [];
}

// Copies the two hand-built guidance pages into the page tree as content-type nodes under the
// existing "guidance" root: "Landing test" at guidance/landing-test and "KS4 test" at
// guidance/ks4-test. Each page's widget tree is rebuilt from its CMS content blocks, written to the
// working draft and published, so it renders through the catch-all PageController at its slug. The
// hand-built originals are left untouched.
public sealed class GuidanceContentCopyService
{
    private readonly IPageNodeService _pageNodes;
    private readonly IContentBlockService _blocks;

    public GuidanceContentCopyService(IPageNodeService pageNodes, IContentBlockService blocks)
    {
        _pageNodes = pageNodes;
        _blocks = blocks;
    }

    public async Task<GuidanceContentCopyResult> CopyAsync(string? userId)
    {
        var guidance = await _pageNodes.GetNodeByPathAsync("guidance");
        if (guidance is null)
            return new GuidanceContentCopyResult { GuidanceRootMissing = true };

        var result = new GuidanceContentCopyResult();

        await CopyPageAsync(
            guidance.Id, "landing-test", "Landing test",
            await GuidanceContentTreeBuilder.BuildLandingAsync(_blocks), userId, result);

        await CopyPageAsync(
            guidance.Id, "ks4-test", "KS4 test",
            await GuidanceContentTreeBuilder.BuildKs4Async(_blocks), userId, result);

        return result;
    }

    private async Task CopyPageAsync(
        Guid guidanceId, string segment, string title,
        IReadOnlyList<ContentNode> tree, string? userId, GuidanceContentCopyResult result)
    {
        var path = $"guidance/{segment}";
        var existing = await _pageNodes.GetNodeByPathAsync(path);

        Guid nodeId;
        if (existing is not null)
        {
            nodeId = existing.Id;
            result.Updated.Add(path);
        }
        else
        {
            var created = await _pageNodes.CreatePageAsync(guidanceId, segment, title, "content", userId);
            nodeId = created.Id;
            result.Created.Add(path);
        }

        var json = ContentPageJson.Serialize(tree);
        await _pageNodes.SaveWorkingContentAsync(nodeId, json, "", userId);
        await _pageNodes.PublishDraftAsync(nodeId, userId);
    }
}
