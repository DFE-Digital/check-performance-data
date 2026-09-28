using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// Pins the app logs page's filter layout: the four filters stack in one two-thirds column, like
// the audit log and the Observability filter forms, rather than four one-quarter cells side by
// side. The field names are pinned too, because the pager and the CSV link replay them from the
// query string.
public sealed class AppLogsViewSourceTests
{
    private static string View(string name) => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "AppLogs", name));

    // Just the filter form, so layout asserts can't be tripped by markup elsewhere on the page.
    private static string FilterForm(string view)
    {
        var start = view.IndexOf("<form method=\"get\" action=\"/admin/system-administration/logs\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "filter form not found");
        var end = view.IndexOf("</form>", start, StringComparison.Ordinal);
        Assert.True(end > start, "filter form not closed");
        return view[start..end];
    }

    [Fact]
    public void Filters_are_stacked_in_one_two_thirds_column_not_side_by_side()
    {
        var form = FilterForm(View("Index.cshtml"));
        Assert.Equal(1, form.Split("govuk-grid-column-two-thirds").Length - 1);
        Assert.DoesNotContain("govuk-grid-column-one-quarter", form);
        Assert.Equal(5, form.Split("<div class=\"govuk-form-group\">").Length - 1);
        Assert.Equal(2, form.Split("govuk-select govuk-!-width-full").Length - 1);
        Assert.Equal(2, form.Split("govuk-input govuk-input--width-10").Length - 1);
        // Stacked groups already carry their own bottom margin, so the old top-margin gap goes.
        Assert.DoesNotContain("govuk-!-margin-top-4", form);

        // The column opens before the first field and closes (with its row) straight after the
        // buttons, so every field and the buttons sit inside it.
        var columnOpen = Regex.Match(form, "<div class=\"govuk-grid-row\">\\s*<div class=\"govuk-grid-column-two-thirds\">");
        Assert.True(columnOpen.Success, "row > two-thirds column opener not found");
        Assert.True(columnOpen.Index < form.IndexOf("name=\"level\"", StringComparison.Ordinal));
        Assert.Matches(new Regex("<div class=\"govuk-button-group\">[\\s\\S]*?</div>\\s*</div>\\s*</div>\\s*$"), form);
        Assert.True(form.IndexOf("name=\"search\"", StringComparison.Ordinal) < form.IndexOf("govuk-button-group", StringComparison.Ordinal));
    }

    [Fact]
    public void Filters_keep_their_names_and_order()
    {
        var view = View("Index.cshtml");
        Assert.Contains("<form method=\"get\" action=\"/admin/system-administration/logs\"", view);

        string[] names = ["name=\"level\"", "name=\"category\"", "name=\"from\"", "name=\"to\"", "name=\"search\""];
        var positions = names.Select(n => view.IndexOf(n, StringComparison.Ordinal)).ToArray();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.OrderBy(p => p), positions);

        Assert.Contains("<label class=\"govuk-label\" for=\"level\">Level</label>", view);
        Assert.Contains("<label class=\"govuk-label\" for=\"category\">Category</label>", view);
        Assert.Contains("<label class=\"govuk-label\" for=\"from\">From (UTC)</label>", view);
        Assert.Contains("<label class=\"govuk-label\" for=\"to\">To (UTC)</label>", view);
        Assert.Contains("<label class=\"govuk-label\" for=\"search\">Search (message + exception, case-insensitive)</label>", view);
        Assert.Contains("Apply filters", view);
        Assert.DoesNotContain("<script", view);
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
