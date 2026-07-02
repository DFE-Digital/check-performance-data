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
            ["heading", "richtext", "divider", "card", "summarylist", "published", "search"],
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
}
