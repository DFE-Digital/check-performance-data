using DfE.CheckPerformanceData.Application.ContentBlocks;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentBlocks;

public sealed class ContentBlockLocationsTests
{
    [Fact]
    public void HomeBlock_MapsToRoot()
    {
        var loc = ContentBlockLocations.Resolve("home-content");
        Assert.Equal("/", loc!.Url);
    }

    [Fact]
    public void LandingBlock_MapsToGuidanceLanding()
    {
        var loc = ContentBlockLocations.Resolve("guidance-landing-ks4-cards");
        Assert.Equal("/guidance", loc!.Url);
    }

    [Theory]
    [InlineData("guidance-ks4-2026-key-dates", "/guidance/2026-ks4-june-checking-exercise#key-dates")]
    [InlineData("guidance-ks4-2026-key-dates-heading", "/guidance/2026-ks4-june-checking-exercise#key-dates")]
    public void Ks4SectionBlock_DeepLinksToAnchor(string key, string expectedUrl)
    {
        var loc = ContentBlockLocations.Resolve(key);
        Assert.Equal(expectedUrl, loc!.Url);
    }

    [Theory]
    [InlineData("guidance-ks4-2026-title")]
    [InlineData("guidance-ks4-2026-intro")]
    [InlineData("guidance-ks4-2026-published")]
    public void Ks4NonSectionBlock_LinksToPageWithoutAnchor(string key)
    {
        var loc = ContentBlockLocations.Resolve(key);
        Assert.Equal("/guidance/2026-ks4-june-checking-exercise", loc!.Url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("e2e-abc-block")]
    [InlineData("some-unknown-key")]
    public void UnknownBlock_ReturnsNull(string key)
    {
        Assert.Null(ContentBlockLocations.Resolve(key));
    }
}
