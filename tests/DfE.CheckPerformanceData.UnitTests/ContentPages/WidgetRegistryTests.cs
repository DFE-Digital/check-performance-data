using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentPages;

// The widget registry is the single catalogue of the content widgets an editor can place: each entry
// carries its type id, the palette label, whether it feeds the auto-nav, and the default props a new
// instance starts with. The renderer and editor both consult it; an unknown type is not placeable.
public class WidgetRegistryTests
{
    [Fact]
    public void All_ListsTheShippedWidgets()
    {
        var types = WidgetRegistry.All.Select(w => w.Type).ToList();

        Assert.Equal(
            ["heading", "richtext", "divider", "card", "summarylist", "published", "search", "results", "pagenav"],
            types);
    }

    [Theory]
    [InlineData("heading")]
    [InlineData("richtext")]
    [InlineData("divider")]
    [InlineData("card")]
    [InlineData("summarylist")]
    [InlineData("published")]
    [InlineData("search")]
    [InlineData("results")]
    [InlineData("pagenav")]
    public void IsKnown_IsTrue_ForEveryRegisteredType(string type) =>
        Assert.True(WidgetRegistry.IsKnown(type));

    [Fact]
    public void IsKnown_IsFalse_ForAnUnregisteredType() =>
        Assert.False(WidgetRegistry.IsKnown("iframe"));

    [Fact]
    public void Get_ReturnsTheDefinition_WithItsPaletteLabel()
    {
        var heading = WidgetRegistry.Get("heading");

        Assert.NotNull(heading);
        Assert.Equal("Heading", heading!.PaletteLabel);
    }

    [Fact]
    public void Get_ReturnsNull_ForAnUnknownType() =>
        Assert.Null(WidgetRegistry.Get("iframe"));

    [Fact]
    public void OnlyTheHeadingWidget_ContributesToNav()
    {
        var contributing = WidgetRegistry.All.Where(w => w.ContributesToNav).Select(w => w.Type);

        Assert.Equal(["heading"], contributing);
    }

    [Fact]
    public void CreateDefaultProps_Heading_DefaultsToLevelTwo()
    {
        var props = WidgetRegistry.CreateDefaultProps("heading");

        Assert.Equal(2, (int)props["level"]!);
        Assert.True(props.ContainsKey("text"));
    }

    [Fact]
    public void CreateDefaultProps_ReturnsAFreshInstanceEachCall_SoTreesDoNotShareNodes()
    {
        var first = WidgetRegistry.CreateDefaultProps("card");
        var second = WidgetRegistry.CreateDefaultProps("card");

        Assert.NotSame(first, second);
    }

    [Fact]
    public void CreateDefaultProps_UnknownType_IsEmpty()
    {
        var props = WidgetRegistry.CreateDefaultProps("iframe");

        Assert.Empty(props);
    }

    // ----- Search widget: the two axes (scope target x instant) -----

    [Fact]
    public void CreateDefaultProps_Search_DefaultsToWholeSite_NotInstant()
    {
        var props = WidgetRegistry.CreateDefaultProps("search");

        Assert.Equal("site", (string)props["searchIn"]!);
        Assert.Equal("false", (string)props["instant"]!);
    }

    [Fact]
    public void CreateDefaultProps_Search_KeepsItsExistingProps()
    {
        // The new props are additive: a widget placed before they existed must keep working,
        // so nothing that was already in the schema may be dropped.
        var props = WidgetRegistry.CreateDefaultProps("search");

        foreach (var key in new[] { "label", "placeholder", "action", "buttonText", "scope" })
            Assert.True(props.ContainsKey(key), $"search default props lost '{key}'.");
    }

    [Fact]
    public void CreateDefaultProps_Search_CarriesNoResultsCopy()
    {
        var props = WidgetRegistry.CreateDefaultProps("search");

        Assert.False(string.IsNullOrWhiteSpace((string?)props["noResultsText"]));
    }

    [Fact]
    public void CreateDefaultProps_PageNav_TicksH2AndH3Only()
    {
        var props = WidgetRegistry.CreateDefaultProps("pagenav");

        Assert.Equal("true", (string)props["h2"]!);
        Assert.Equal("true", (string)props["h3"]!);
        foreach (var off in new[] { "h1", "h4", "h5", "h6" })
            Assert.Equal("false", (string)props[off]!);
    }
}
