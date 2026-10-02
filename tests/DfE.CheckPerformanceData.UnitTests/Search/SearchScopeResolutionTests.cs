using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Application.Search;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Search;

// A request can name pages by path (?scope=) or by token (?pages=). Both end up as one path
// scope, looked up against the pages that exist now.
public class SearchScopeResolutionTests
{
    private static readonly Guid Help = new("00000000-cd94-4a01-8f01-000000000003");
    private static readonly Guid Post16 = new("00000000-cd94-4a01-8f01-0000000000a1");

    private readonly IPageNodeRepository _pages = Substitute.For<IPageNodeRepository>();

    public SearchScopeResolutionTests()
    {
        _pages.GetTreeAsync().Returns(
        [
            new PageNodeTreeItemDto { Id = Help, Segment = "help", Path = "help", Title = "Help", PageType = "content" },
            new PageNodeTreeItemDto { Id = Post16, Segment = "post-16", Path = "guidance/post-16", Title = "Post-16", PageType = "content" },
        ]);
    }

    [Fact]
    public async Task NoScopeAndNoPages_IsTheWholeSite_WithoutLookingUpPages()
    {
        var resolved = await _pages.ResolveSearchScopeAsync(null, " ");

        Assert.Equal(ResolvedSearchScope.WholeSite, resolved);
        await _pages.DidNotReceive().GetTreeAsync();
    }

    // Existing ?scope= links behave exactly as before: normalised, not checked against pages.
    [Fact]
    public async Task PathScopeAlone_IsNormalised_WithoutLookingUpPages()
    {
        var resolved = await _pages.ResolveSearchScopeAsync("/guidance/ks4/, help", null);

        Assert.Equal(new ResolvedSearchScope("guidance/ks4,help", false), resolved);
        await _pages.DidNotReceive().GetTreeAsync();
    }

    [Fact]
    public async Task PageTokens_BecomeThePathsOfTheirPages()
    {
        var resolved = await _pages.ResolveSearchScopeAsync(
            null, $"{PageToken.For(Post16)},{PageToken.For(Help)}");

        Assert.Equal(new ResolvedSearchScope("guidance/post-16,help", false), resolved);
    }

    [Fact]
    public async Task UnknownTokens_AreIgnored()
    {
        var resolved = await _pages.ResolveSearchScopeAsync(null, $"zzzzzzzz,{PageToken.For(Help)}");

        Assert.Equal(new ResolvedSearchScope("help", false), resolved);
    }

    // A widget whose pages have all been deleted must not quietly search the whole site.
    [Fact]
    public async Task OnlyUnknownTokens_NamesNothing()
    {
        var resolved = await _pages.ResolveSearchScopeAsync(null, "zzzzzzzz");

        Assert.Equal(new ResolvedSearchScope(null, true), resolved);
    }

    // Both on one URL: the search covers the pages named by either, paths first.
    [Fact]
    public async Task PathsAndTokensTogether_AreJoined_WithoutRepeats()
    {
        var resolved = await _pages.ResolveSearchScopeAsync(
            "help,guidance/ks4", $"{PageToken.For(Post16)},{PageToken.For(Help)}");

        Assert.Equal(new ResolvedSearchScope("help,guidance/ks4,guidance/post-16", false), resolved);
    }

    [Fact]
    public async Task PathsWithOnlyUnknownTokens_KeepThePaths()
    {
        var resolved = await _pages.ResolveSearchScopeAsync("help", "zzzzzzzz");

        Assert.Equal(new ResolvedSearchScope("help", false), resolved);
    }
}
