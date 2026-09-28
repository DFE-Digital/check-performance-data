using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// Pins the app logs page's filter layout: the four filters stack in one two-thirds column, like
// the audit log and the Observability filter forms, rather than four one-quarter cells side by
// side. The field names are pinned too, because the pager and the CSV link replay them from the
// query string.
public sealed class AppLogsViewSourceTests
{
    private static string View(string name) => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "AppLogs", name));

    [Fact]
    public void Filters_are_stacked_in_one_two_thirds_column_not_side_by_side()
    {
        var view = View("Index.cshtml");
        Assert.Equal(1, view.Split("govuk-grid-column-two-thirds").Length - 1);
        Assert.DoesNotContain("govuk-grid-column-one-quarter", view);
        Assert.Equal(2, view.Split("govuk-select govuk-!-width-full").Length - 1);
        Assert.Equal(2, view.Split("govuk-input govuk-input--width-10").Length - 1);
        // Stacked groups already carry their own bottom margin, so the old top-margin gap goes.
        Assert.DoesNotContain("govuk-!-margin-top-4", view);
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
        Assert.Contains("Apply filters", view);
        Assert.DoesNotContain("<script", view);
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
