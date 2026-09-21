using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// Pins the runs history page's contracts (AB#294590) that the always-on CI gate cannot see at
// E2E tier: a plain GET filter form with a real button (no script), both empty states, the numeric
// records column, the row links with their hidden suffix, and the shared pager.
public sealed class EgressRunsViewSourceTests
{
    private static string View(string name) => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "EgressRuns", name));

    [Fact]
    public void The_runs_views_render_under_the_admin_layout()
        => Assert.Contains("Layout = \"_AdminLayout\";", View("_ViewStart.cshtml"));

    [Fact]
    public void The_page_renders_exactly_one_h1_and_the_tickets_heading()
    {
        var view = View("Index.cshtml");
        Assert.Equal(1, view.Split("<h1 ").Length - 1);
        Assert.Contains(">Egress runs</h1>", view);
        Assert.Contains("A history of all data egress runs.", view);
    }

    [Fact]
    public void Filters_are_a_plain_get_form_with_two_selects_and_a_button()
    {
        var view = View("Index.cshtml");
        Assert.Contains("<form method=\"get\" action=\"/admin/egress/runs\"", view);
        Assert.Contains("data-testid=\"egress-runs-filters\"", view);
        Assert.Contains("name=\"windowId\"", view);
        Assert.Contains("name=\"status\"", view);
        Assert.Contains("Filter by checking window", view);
        Assert.Contains("Filter by status", view);
        Assert.Contains(">All windows</option>", view);
        Assert.Contains(">All statuses</option>", view);
        Assert.Contains("govuk-button--secondary", view);
        Assert.Contains("Apply filters", view);
        Assert.DoesNotContain("name=\"page\"", view);      // applying a filter returns to page 1
        Assert.DoesNotContain("<script", view);
        Assert.DoesNotContain("onchange", view);
    }

    [Fact]
    public void The_status_options_come_from_the_one_outcome_list()
    {
        var view = View("Index.cshtml");
        Assert.Contains("EgressRunOutcomes.All", view);
        Assert.Contains("EgressRunOutcomes.Label(", view);
    }

    [Fact]
    public void Both_empty_states_are_stated_and_neither_renders_a_table()
    {
        var view = View("Index.cshtml");
        Assert.Contains("Model.IsEmpty", view);
        Assert.Contains("data-testid=\"egress-runs-empty\"", view);
        Assert.Contains("No egress runs match the filters you have applied.", view);
        Assert.Contains("There are no egress runs yet.", view);
        // The table lives in the else branch: the testid must appear after the empty-state markup.
        Assert.True(view.IndexOf("data-testid=\"egress-runs\"", StringComparison.Ordinal) > view.IndexOf("data-testid=\"egress-runs-empty\"", StringComparison.Ordinal));
    }

    [Fact]
    public void The_table_has_the_tickets_columns_with_a_numeric_records_column()
    {
        var view = View("Index.cshtml");
        Assert.Contains("data-testid=\"egress-runs\"", view);
        foreach (var header in new[] { "Checking window", "Output types", "Records", "Run by", "Started" })
            Assert.Contains($">{header}</th>", view);
        Assert.Contains("govuk-table__header--numeric", view);
        Assert.Contains("govuk-table__cell--numeric\">@row.RecordsTransferred</td>", view);
        Assert.Contains("@row.OutcomeTagClass", view);
        Assert.Contains("@row.OutcomeLabel", view);
        Assert.Contains("govuk-tag--blue govuk-!-margin-bottom-1\">@label</strong>", view);   // one tag per output type, spaced when they stack
        Assert.Contains("@row.StartedAtUtc.ToString(\"d MMM yyyy HH:mm\") UTC", view);
    }

    // Repeated link text ("Resume" / "View" on every row) needs a hidden suffix naming the row —
    // the accessibility audit's repeated-link rule.
    [Fact]
    public void Row_links_go_to_resume_and_carry_a_visually_hidden_suffix()
    {
        var view = View("Index.cshtml");
        Assert.Contains("@if (row.LinkText is not null)", view);
        Assert.Contains("href=\"/admin/egress/runs/@row.Id\">@row.LinkText<span class=\"govuk-visually-hidden\">", view);
        Assert.Contains("run for @row.WindowTitle started", view);
    }

    [Fact]
    public void The_shared_pager_is_invoked_unconditionally_with_the_filters_kept()
    {
        var view = View("Index.cshtml");
        Assert.Contains("Component.InvokeAsync(\"Pager\"", view);
        Assert.Contains("currentPage = Model.Page", view);
        Assert.Contains("totalPages = Model.TotalPages", view);
        Assert.Contains("ariaLabel = \"Egress runs pages\"", view);
        Assert.Contains("PageLink(int ", view);
        Assert.Contains("windowId=", view);
        Assert.Contains("status=", view);
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
