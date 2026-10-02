using DfE.CheckPerformanceData.Application.Search;

namespace DfE.CheckPerformanceData.Application.UnitTests.Search;

// A page token is the first eight characters of base62(SHA-256(page id, big-endian bytes)). It
// is what a search link carries instead of a page path, so it must never change for a page and
// must stay short and URL-safe.
public class PageTokenTests
{
    // Worked out independently of this code, from the page id's RFC 4122 byte order.
    [Theory]
    [InlineData("00000000-cd94-4a01-8f01-000000000001", "fiSE59QT")]
    [InlineData("00000000-cd94-4a01-8f01-000000000002", "QxpbTxwJ")]
    [InlineData("00000000-cd94-4a01-8f01-000000000004", "Sbfp6xsR")]
    [InlineData("00000000-cd94-4a01-8f01-0000000e0002", "7a5dkMse")]
    [InlineData("00000000-0000-0000-0000-000000000000", "D6h7p6dP")]
    public void For_KnownPageIds_GiveTheKnownTokens(string id, string expected)
    {
        Assert.Equal(expected, PageToken.For(new Guid(id)));
    }

    [Fact]
    public void For_IsTheSameEveryTime()
    {
        var id = Guid.NewGuid();
        Assert.Equal(PageToken.For(id), PageToken.For(id));
    }

    [Fact]
    public void For_IsEightUrlSafeCharacters()
    {
        for (var i = 0; i < 500; i++)
        {
            var token = PageToken.For(Guid.NewGuid());
            Assert.Equal(8, PageToken.Length);
            Assert.Equal(PageToken.Length, token.Length);
            Assert.All(token, c => Assert.True(char.IsAsciiLetterOrDigit(c), $"'{c}' in {token}"));
            Assert.Equal(token, Uri.EscapeDataString(token));
        }
    }

    // The seeded page ids differ only in their last few digits; their tokens must still differ.
    [Fact]
    public void For_IdsSharingALongPrefix_GiveDistinctTokens()
    {
        var tokens = Enumerable.Range(0, 4096)
            .Select(n => PageToken.For(new Guid($"00000000-cd94-4a01-8f01-{n:x12}")))
            .ToList();

        Assert.Equal(tokens.Count, tokens.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , ,")]
    public void ParseList_NothingUsable_IsEmpty(string? raw)
    {
        Assert.Empty(PageToken.ParseList(raw));
        Assert.Null(PageToken.Normalise(raw));
    }

    // Tokens are case-sensitive (base62), so differently-cased tokens are different tokens.
    [Fact]
    public void ParseList_TrimsAndDropsRepeats_KeepingCase()
    {
        Assert.Equal(["fiSE59QT", "QxpbTxwJ", "fise59qt"],
            PageToken.ParseList(" fiSE59QT ,QxpbTxwJ,fiSE59QT,fise59qt,"));
        Assert.Equal("fiSE59QT,QxpbTxwJ", PageToken.Normalise("fiSE59QT, QxpbTxwJ"));
    }
}
