using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentPages;

// The editor's widget edit form posts flat string fields; WidgetPropsBuilder turns them into a typed
// props object using the registry's default props as the schema — numeric props (heading level) are
// coerced to numbers, a bad number falls back to the default, and the summary-list rows textarea
// (one "key|value" per line) becomes a structured array.
public class WidgetPropsBuilderTests
{
    [Fact]
    public void Build_Heading_CoercesLevelToNumber_AndKeepsText()
    {
        var props = WidgetPropsBuilder.Build("heading",
            new Dictionary<string, string?> { ["level"] = "3", ["text"] = "Get support" });

        Assert.Equal(3, (int)props["level"]!);
        Assert.Equal("Get support", (string)props["text"]!);
    }

    [Fact]
    public void Build_Heading_FallsBackToDefaultLevel_WhenNotANumber()
    {
        var props = WidgetPropsBuilder.Build("heading",
            new Dictionary<string, string?> { ["level"] = "huge", ["text"] = "X" });

        Assert.Equal(2, (int)props["level"]!);   // registry default
    }

    [Fact]
    public void Build_Card_TakesAllStringFields()
    {
        var props = WidgetPropsBuilder.Build("card",
            new Dictionary<string, string?> { ["title"] = "Apply", ["body"] = "Start", ["href"] = "/apply" });

        Assert.Equal("Apply", (string)props["title"]!);
        Assert.Equal("Start", (string)props["body"]!);
        Assert.Equal("/apply", (string)props["href"]!);
    }

    [Fact]
    public void Build_RichText_TakesHtml()
    {
        var props = WidgetPropsBuilder.Build("richtext",
            new Dictionary<string, string?> { ["html"] = "<p>Hello</p>" });

        Assert.Equal("<p>Hello</p>", (string)props["html"]!);
    }

    [Fact]
    public void Build_Unknown_FieldsAreIgnored()
    {
        var props = WidgetPropsBuilder.Build("heading",
            new Dictionary<string, string?> { ["text"] = "X", ["bogus"] = "y" });

        Assert.False(props.ContainsKey("bogus"));
    }

    [Fact]
    public void Build_SummaryList_ParsesRowsTextarea()
    {
        var props = WidgetPropsBuilder.Build("summarylist",
            new Dictionary<string, string?> { ["rows"] = "Opens|1 June\nCloses|2 July" });

        var rows = props["rows"]!.AsArray();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Opens", (string)rows[0]!["key"]!);
        Assert.Equal("1 June", (string)rows[0]!["value"]!);
        Assert.Equal("Closes", (string)rows[1]!["key"]!);
        Assert.Equal("2 July", (string)rows[1]!["value"]!);
    }

    [Fact]
    public void Build_SummaryList_SkipsBlankAndMalformedLines()
    {
        var props = WidgetPropsBuilder.Build("summarylist",
            new Dictionary<string, string?> { ["rows"] = "Opens|1 June\n\nnopipe\nCloses|2 July" });

        var rows = props["rows"]!.AsArray();
        Assert.Equal(2, rows.Count);
    }
}
