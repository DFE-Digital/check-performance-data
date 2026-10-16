using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Application.UnitTests.PageTree;

public class PageSegmentTests
{
    [Theory]
    [InlineData("help")]
    [InlineData("wiki-sandbox")]
    [InlineData("get-me-started-2")]
    [InlineData("a1b2")]
    public void IsValid_KebabCase_IsTrue(string segment) => Assert.True(PageSegment.IsValid(segment));

    [Theory]
    [InlineData("")]
    [InlineData("Help")]
    [InlineData("help-")]
    [InlineData("-help")]
    [InlineData("help--me")]
    [InlineData("help/me")]
    [InlineData("help me")]
    [InlineData("help.md")]
    [InlineData("help\n")]
    [InlineData(null)]
    public void IsValid_AnythingElse_IsFalse(string? segment) => Assert.False(PageSegment.IsValid(segment));

    [Theory]
    [InlineData("guidance-copy-17-Sep", "guidance-copy-17-sep")]
    [InlineData("  My-Page ", "my-page")]
    [InlineData("help", "help")]
    [InlineData(null, "")]
    public void Normalise_TrimsAndLowercases(string? segment, string expected) =>
        Assert.Equal(expected, PageSegment.Normalise(segment));

    [Fact]
    public void Normalise_DoesNotRepairOtherMistakes() =>
        Assert.False(PageSegment.IsValid(PageSegment.Normalise("My Page")));
}
