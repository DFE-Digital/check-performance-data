using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#297848: pins the qualification search page's no-JS-usability markup — the QAN select must
// render every option grouped by AO (not just the ones for a pre-chosen AO) so the page works
// with JavaScript off, and both fields must anchor an error-summary link.
public sealed class QualificationSearchViewSourceTests
{
    private static string ViewSource()
        => File.ReadAllText(Path.Combine(
            RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Journey", "QualificationSearch.cshtml"));

    [Fact]
    public void The_QAN_select_is_grouped_by_AO_so_the_page_works_without_javascript()
    {
        var view = ViewSource();

        Assert.Contains("<optgroup", view);
        Assert.Contains("@foreach (var group in Model.Qualifications.GroupBy(q => q.AwardingOrganisation))", view);
    }

    [Fact]
    public void Both_fields_anchor_an_error_summary_link()
    {
        var view = ViewSource();

        Assert.Contains("href=\"#selectedAo\"", view);
        Assert.Contains("href=\"#selectedQan\"", view);
    }

    [Fact]
    public void The_AO_selects_first_option_is_the_empty_placeholder()
    {
        var view = ViewSource();

        var selectIndex = view.IndexOf("id=\"selectedAo\"", StringComparison.Ordinal);
        var afterSelect = view[selectIndex..];
        var firstOptionIndex = afterSelect.IndexOf("<option", StringComparison.Ordinal);

        Assert.Contains("value=\"\"", afterSelect[firstOptionIndex..(firstOptionIndex + 40)]);
    }

    private static string RepoRoot
    {
        get
        {
            var thisFile = ThisFilePath();
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
        }
    }

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
