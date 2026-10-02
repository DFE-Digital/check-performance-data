using DfE.CheckPerformanceData.Application.Search;

namespace DfE.CheckPerformanceData.Application.UnitTests.Search;

public class PageTokenIndexTests
{
    private static readonly Guid Guidance = new("00000000-cd94-4a01-8f01-000000000004");
    private static readonly Guid Help = new("00000000-cd94-4a01-8f01-000000000003");
    private static readonly Guid Post16 = new("00000000-cd94-4a01-8f01-0000000000a1");

    private static PageTokenIndex Index(Func<Guid, string>? tokenFor = null) => new(
        [(Guidance, "guidance"), (Help, "help"), (Post16, "guidance/post-16")], tokenFor);

    [Fact]
    public void PathsFor_FindsEachPageByItsToken_InTokenOrder()
    {
        Assert.Equal(["guidance/post-16", "help"],
            Index().PathsFor([PageToken.For(Post16), PageToken.For(Help)]));
    }

    [Fact]
    public void PathsFor_IgnoresUnknownTokens()
    {
        Assert.Equal(["help"], Index().PathsFor(["zzzzzzzz", PageToken.For(Help), "not a token"]));
        Assert.Empty(Index().PathsFor(["zzzzzzzz"]));
    }

    // Tokens are case-sensitive: a token with its case changed is a different token.
    [Fact]
    public void PathsFor_MatchesTokensExactly()
    {
        var token = PageToken.For(Help);
        var flipped = new string(token
            .Select(c => char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c))
            .ToArray());

        Assert.Empty(Index().PathsFor([flipped]));
    }

    // Two pages sharing a token is vanishingly unlikely, but if it happens both are searched,
    // so a clash can only widen a search, never hide a page someone picked.
    [Fact]
    public void PathsFor_ATokenTwoPagesShare_NamesBothPages()
    {
        var index = Index(id => id == Help ? "Unique01" : "Clash001");

        Assert.Equal(["guidance", "guidance/post-16"], index.PathsFor(["Clash001"]));
        Assert.Equal(["help"], index.PathsFor(["Unique01"]));
    }

    [Fact]
    public void PathsFor_ATokenGivenTwice_NamesItsPageOnce()
    {
        Assert.Equal(["help"], Index().PathsFor([PageToken.For(Help), PageToken.For(Help)]));
    }
}
