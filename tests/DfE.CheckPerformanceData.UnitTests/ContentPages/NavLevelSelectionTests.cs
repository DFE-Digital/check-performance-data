using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentPages;

// Turning the page-nav widget's tick boxes into a set of heading levels. The distinction that
// matters is between a widget that was never asked (placed before the tick boxes existed, which
// gets the default pair) and one whose author unticked everything (which gets nothing).
public sealed class NavLevelSelectionTests
{
    private static WidgetNode Nav(JsonObject? props) => new() { Type = "pagenav", Props = props };

    [Fact]
    public void AWidgetWithNoLevelProps_GetsTheDefaultPair()
    {
        var widget = Nav(new JsonObject { ["mode"] = "headings" });

        Assert.Equal([2, 3], NavLevelSelection.For(widget));
    }

    [Fact]
    public void NullProps_GetTheDefaultPair()
    {
        Assert.Equal([2, 3], NavLevelSelection.For(Nav(null)));
    }

    [Fact]
    public void TickedLevels_AreReturnedInOrder()
    {
        var widget = Nav(new JsonObject
        {
            ["h1"] = "false", ["h2"] = "true", ["h3"] = "false",
            ["h4"] = "true", ["h5"] = "false", ["h6"] = "false",
        });

        Assert.Equal([2, 4], NavLevelSelection.For(widget));
    }

    [Fact]
    public void UntickingEverything_MeansNoLevels_NotTheDefault()
    {
        var widget = Nav(new JsonObject
        {
            ["h1"] = "false", ["h2"] = "false", ["h3"] = "false",
            ["h4"] = "false", ["h5"] = "false", ["h6"] = "false",
        });

        Assert.Empty(NavLevelSelection.For(widget));
    }

    [Fact]
    public void ARealBooleanIsReadTheSameAsTheFormsString()
    {
        // Registry defaults arrive as JSON values; a form post arrives as "true"/"false" text.
        var widget = Nav(new JsonObject { ["h2"] = true, ["h3"] = false });

        Assert.Equal([2], NavLevelSelection.For(widget));
    }
}
