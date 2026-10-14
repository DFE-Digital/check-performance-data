namespace DfE.CheckPerformanceData.Application.UnitTests.ContentPages;

// Source contract for landing on a page chosen from an instant search of a group of pages.
// The term travels in the page URL as ?highlight=, and the content page loads the instant-search
// script whenever it is present, so the page can mark the word and jump to it the same way a
// search of the page itself does. Behaviour is covered end to end in SearchByPageTokenE2ETests.
public sealed class SearchLandingContractTests
{
    private static readonly string Script = Read("src", "DfE.CheckPerformanceData.Web", "wwwroot", "js", "instant-search.js");
    private static readonly string ContentView = Read("src", "DfE.CheckPerformanceData.Web", "Views", "Page", "Content.cshtml");

    [Fact]
    public void ChoosingAPage_CarriesTheSearchedTermInItsUrl()
    {
        Assert.Contains("var HIGHLIGHT_PARAM = 'highlight';", Script);
        Assert.Contains("window.location.assign(withHighlight(result.url, lastQuery))", Script);
    }

    [Fact]
    public void TheScript_MarksAndJumpsToTheTermItArrivesWith()
    {
        Assert.Contains("function landOnHighlight()", Script);
        Assert.Contains(".get(HIGHLIGHT_PARAM)", Script);
    }

    [Fact]
    public void ContentPage_LoadsTheScript_OnlyWhenATermArrives()
    {
        var guard = ContentView.IndexOf("Context.Request.Query[\"highlight\"]", StringComparison.Ordinal);
        var script = ContentView.IndexOf("~/js/instant-search.js", StringComparison.Ordinal);

        Assert.True(guard >= 0, "Content.cshtml does not read the highlight parameter.");
        Assert.True(script > guard, "instant-search.js must load inside the highlight guard.");
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetDirectories("src").Length == 0) dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("Could not locate the solution root.");
        return File.ReadAllText(Path.Combine([dir.FullName, .. parts]));
    }
}
