using DfE.CheckPerformanceData.Application.Wiki;

namespace DfE.CheckPerformanceData.Application.UnitTests.Wiki;

public sealed class WikiSlugTests
{
    [Theory]
    [InlineData("New Page", "new-page")]
    [InlineData("UPPER Case", "upper-case")]
    // Removed punctuation leaves multiple spaces → collapsed to a single dash.
    [InlineData("Schools & Colleges", "schools-colleges")]
    // Existing dashes plus surrounding spaces collapse via the multiple-dash stage.
    [InlineData("A - B", "a-b")]
    // Leading/trailing punctuation yields leading/trailing dashes that are trimmed off.
    [InlineData("& Hello &", "hello")]
    // Tabs and newlines are whitespace and collapse like spaces.
    [InlineData("a\tb\nc", "a-b-c")]
    public void Generate_NormalisesTitleToSlug(string title, string expected)
    {
        Assert.Equal(expected, WikiSlug.Generate(title));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("###")]
    public void Generate_WhenNothingSlugSafeRemains_ReturnsEmpty(string title)
    {
        Assert.Equal("", WikiSlug.Generate(title));
    }

    [Fact]
    public void Generate_StripsAccentedCharacters()
    {
        // Pins current behaviour: accents are not transliterated, just removed.
        Assert.Equal("caf-crme", WikiSlug.Generate("Café Crème"));
    }
}
