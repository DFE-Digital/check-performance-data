namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// Source-file assertion pattern (mirrors LayoutViewSourceTests): reads
// Views/WhatToChange/Index.cshtml from disk and asserts static Razor-source facts.
//
// docs/ks2-journeys.md, decision 1: the radios come from Model.AvailableChanges, which lists the changes that
// have a flow file for the window type. The view must not name a change or a window type itself,
// or one radio could show where no flow file serves it.
public sealed class WhatToChangeViewSourceTests
{
    private static string ReadViewSource(string relativePath)
    {
        var viewsDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "DfE.CheckPerformanceData.Web"));
        return File.ReadAllText(Path.Combine(viewsDir, relativePath));
    }

    [Fact]
    public void WhatToChange_renders_one_radio_per_available_change()
    {
        var source = ReadViewSource("Views/WhatToChange/Index.cshtml");

        Assert.Contains("@foreach (var change in Model.AvailableChanges)", source);
        Assert.Contains("WhatToChangeLabel.For(change, Model.Noun)", source);
    }

    [Theory]
    [InlineData("WhatToChange.Merge")]
    [InlineData("WhatToChange.Include")]
    [InlineData("WhatToChange.Remove")]
    [InlineData("WhatToChange.Add")]
    [InlineData("SupportedWindowTypes")]
    [InlineData("CheckingWindowType")]
    [InlineData("Post16")]
    [InlineData("KS2")]
    [InlineData("KS4")]
    public void WhatToChange_names_no_change_or_window_type(string text)
    {
        var source = ReadViewSource("Views/WhatToChange/Index.cshtml");

        Assert.DoesNotContain(text, source);
    }

    [Fact]
    public void WhatToChange_keeps_the_legend_hint_and_name()
    {
        var source = ReadViewSource("Views/WhatToChange/Index.cshtml");

        Assert.Contains("What @Model.Noun.Singular data would you like to change?", source);
        Assert.Contains("Select one option.", source);
        Assert.Contains("for=\"SelectedWhatToChange\"", source);
    }
}
