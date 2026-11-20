using DfE.CheckPerformanceData.Application.Common;

namespace DfE.CheckPerformanceData.Application.UnitTests.Common;

// Editors type links into widgets. A link is safe when it is a web address (http or https) or a
// path on this site. Anything else could run script or send a visitor somewhere unexpected.
public sealed class SafeLinkTests
{
    [Theory]
    [InlineData("https://www.gov.uk/guidance")]
    [InlineData("http://example.com/page?a=1#b")]
    [InlineData("HTTPS://WWW.GOV.UK")]
    [InlineData("/guidance/key-dates")]
    [InlineData("/")]
    [InlineData("#section")]
    public void IsSafe_AcceptsWebAddressesAndSitePaths(string href) =>
        Assert.True(SafeLink.IsSafe(href));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData(" javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("vbscript:msgbox")]
    [InlineData("mailto:someone@example.com")]
    [InlineData("//evil.example.com/path")]
    [InlineData("/\evil.example.com")]
    [InlineData("https://")]
    [InlineData("https:///nohost")]
    [InlineData("guidance/relative-without-slash")]
    [InlineData("/path with space")]
    [InlineData("/path\twith\ttab")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsSafe_RefusesEverythingElse(string? href) =>
        Assert.False(SafeLink.IsSafe(href));
}
