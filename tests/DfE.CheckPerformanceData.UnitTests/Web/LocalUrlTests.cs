using DfE.CheckPerformanceData.Web.Common;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class LocalUrlTests
{
    [Theory]
    [InlineData("/guidance", "/guidance")]
    [InlineData("/guidance/ks4?x=1", "/guidance/ks4?x=1")]
    [InlineData("/", "/")]
    public void SafeOrNull_returns_local_paths_unchanged(string input, string expected)
        => Assert.Equal(expected, LocalUrl.SafeOrNull(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("https://evil.example/x")]
    [InlineData("javascript:alert(1)")]
    [InlineData("relative/path")]
    public void SafeOrNull_returns_null_for_unsafe_or_empty(string? input)
        => Assert.Null(LocalUrl.SafeOrNull(input));
}
