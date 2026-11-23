using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// Pins the per-window rules page contracts (#800, contracts/window-rules-page.md): the header
// carries the window title, PupilData exercise start date and rules version; each outcome
// gets one summary card whose branches show the described condition; window-relative dates
// resolve against the PupilData exercise start date via the window-aware describer; and a
// missing rules blob shows the "no rules configured" state. Hostless pattern, same as
// WindowSummaryViewSourceTests: read the .cshtml as text and assert on the source.
public sealed class WindowRulesViewSourceTests
{
    private static string View() => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "WindowAdmin", "Rules.cshtml"));

    // The text from the first occurrence of `from` up to the next occurrence of `to` after it.
    private static string Between(string view, string from, string to)
    {
        var start = view.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{from}' not found in Rules.cshtml");
        var end = view.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"'{to}' not found after '{from}' in Rules.cshtml");
        return view[start..end];
    }

    [Fact]
    public void The_title_matches_the_single_h1()
    {
        // Accessibility rule: ViewData["Title"] holds the same words as the page's one <h1>,
        // so the layout's error prefix and the document title stay in step.
        var view = View();
        Assert.Contains("ViewBag.Title = Model.Title;", view);
        Assert.Contains("<h1 class=\"govuk-heading-xl\">@Model.Title</h1>", view);
        Assert.Equal(1, view.Split("<h1", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void The_breadcrumbs_render_before_main()
    {
        // The breadcrumb is repeated navigation, so it must sit in the layout's Breadcrumbs
        // section, which renders before <main>, not inline in the page body (audit #377).
        var view = View();
        Assert.Contains("@section Breadcrumbs {", view);
        Assert.Contains("govuk-breadcrumbs", view);
    }

    [Fact]
    public void The_header_shows_the_window_start_date_and_the_rules_version()
    {
        var view = View();
        var header = Between(view, "govuk-summary-list", "</govuk-summary-list>");
        Assert.Contains("Pupil data start date", header);
        Assert.Contains("Model.StartDate", header);
        Assert.Contains("Rules version", header);
        Assert.Contains("Model.RulesVersion", header);
    }

    [Fact]
    public void A_missing_start_date_or_rules_version_shows_not_set()
    {
        // Both header rows can be empty: a window without a PupilData exercise has no anchor,
        // and a deployment with no rules blob has no version. Each falls back to "not set"
        // rather than rendering an empty value or throwing.
        var view = View();
        var header = Between(view, "govuk-summary-list", "</govuk-summary-list>");
        Assert.Equal(2, header.Split("\"not set\"", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Each_outcome_gets_one_summary_card_per_branch()
    {
        // One GOV.UK summary card per outcome; each branch inside the card lists its
        // described condition through the read-only predicate partial.
        var view = View();
        var cards = Between(view, "foreach (var outcome in Model.Outcomes)", "</govuk-summary-card>");
        Assert.Contains("govuk-summary-card", cards);
        Assert.Contains("@outcome.Label", cards);
        Assert.Contains("foreach (var branch in outcome.Branches)", cards);
        Assert.Contains("@branch.Id", cards);
        Assert.Contains("_PredicateNode.cshtml", cards);
    }

    [Fact]
    public void The_branches_carry_the_described_condition_prebuilt()
    {
        // The view stays thin: it renders each branch's described condition node as-is.
        // The window-aware describer call lives in the view model factory, not here.
        var view = View();
        Assert.Contains("_PredicateNode.cshtml", view);
        Assert.Contains("branch.Condition", view);
        Assert.DoesNotContain("PredicateDescriber", view);
    }

    [Fact]
    public void A_missing_rules_blob_shows_the_empty_state()
    {
        var view = View();
        var empty = Between(view, "@if (!Model.HasRules)", "</div>");
        Assert.Contains("no rules configured", empty);
    }

    [Fact]
    public void The_page_uses_gov_uk_components_and_no_inline_handlers()
    {
        var view = View();
        Assert.Contains("govuk-summary-list", view);
        Assert.Contains("govuk-summary-card", view);
        Assert.Contains("govuk-back-link", view);
        Assert.DoesNotContain("onclick", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_back_link_returns_to_the_window_summary()
    {
        var view = View();
        Assert.Contains("/admin/windows/summary/", view);
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
