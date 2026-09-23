using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// Pins the audit log page's contracts (AB#294592) that the always-on CI gate cannot see at E2E
// tier: a plain GET filter form with three selects and a real button (no script), both empty
// states, the five columns, the tag bindings, the pager, the export button — and that the page is
// read-only: no POST, no Delete/Edit/Amend, and no payload column.
public sealed class AuditLogViewSourceTests
{
    private static string View(string name) => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "AuditLog", name));

    [Fact]
    public void The_audit_log_views_render_under_the_admin_layout()
        => Assert.Contains("Layout = \"_AdminLayout\";", View("_ViewStart.cshtml"));

    [Fact]
    public void The_page_renders_exactly_one_h1_and_the_heading()
    {
        var view = View("Index.cshtml");
        Assert.Equal(1, view.Split("<h1 ").Length - 1);
        Assert.Contains(">Audit log</h1>", view);
        Assert.Contains("A record of data egress runs and other administrative activity.", view);
        Assert.Contains("href=\"/admin\"", view);
    }

    [Fact]
    public void Filters_are_a_plain_get_form_with_three_selects_and_a_button()
    {
        var view = View("Index.cshtml");
        Assert.Contains("<form method=\"get\" action=\"/admin/audit-log\"", view);
        Assert.Contains("data-testid=\"audit-log-filters\"", view);
        Assert.Contains("name=\"activity\"", view);
        Assert.Contains("name=\"windowId\"", view);
        Assert.Contains("name=\"status\"", view);
        Assert.Contains("Filter by activity", view);
        Assert.Contains("Filter by checking window", view);
        Assert.Contains("Filter by status", view);
        Assert.Contains(">All activity</option>", view);
        Assert.Contains(">All windows</option>", view);
        Assert.Contains(">All statuses</option>", view);
        Assert.Contains("govuk-button--secondary", view);
        Assert.Contains("Apply filters", view);
        Assert.DoesNotContain("name=\"page\"", view);      // applying a filter returns to page 1
        Assert.DoesNotContain("<script", view);
    }

    [Fact]
    public void Both_empty_states_are_worded_and_nothing_tabular_renders_in_them()
    {
        var view = View("Index.cshtml");
        Assert.Contains("data-testid=\"audit-log-empty\"", view);
        Assert.Contains("No audit records match the filters you have applied.", view);
        Assert.Contains("There are no audit records yet.", view);
        Assert.Contains("@if (Model.IsEmpty)", view);
    }

    [Fact]
    public void The_table_has_the_five_columns_and_rows_are_addressable()
    {
        var view = View("Index.cshtml");
        Assert.Contains("data-testid=\"audit-log\"", view);
        Assert.Contains(">User</th>", view);
        Assert.Contains(">Activity</th>", view);
        Assert.Contains(">Checking window</th>", view);
        Assert.Contains(">Time</th>", view);
        Assert.Contains(">Status</th>", view);
        Assert.Contains("data-testid=\"audit-log-row\"", view);
        Assert.Contains("data-entity-type=\"@row.EntityType\"", view);
        Assert.Contains("data-entity-id=\"@row.EntityId\" data-action=\"@row.Action\"", view);
        Assert.Contains("govuk-tag @row.ActivityTagClass\">@row.ActivityLabel</strong>", view);
        Assert.Contains("govuk-tag @row.OutcomeTagClass\">@row.OutcomeLabel</strong>", view);
        Assert.Contains("@row.WindowDetail", view);
        Assert.Contains("@row.ActivityDetail", view);
        Assert.Contains("HH:mm:ss\") UTC", view);
    }

    [Fact]
    public void Paging_and_export_keep_every_filter()
    {
        var view = View("Index.cshtml");
        Assert.Contains("Component.InvokeAsync(\"Pager\"", view);
        Assert.Contains("ariaLabel = \"Audit log pages\"", view);
        Assert.Contains("Model.PageUrl(", view);
        Assert.Contains("href=\"@Model.ExportUrl\"", view);
        Assert.Contains("Export log as CSV", view);
        Assert.Contains("role=\"button\"", view);
    }

    [Fact]
    public void The_page_is_read_only_and_never_renders_a_payload()
    {
        var view = View("Index.cshtml");
        Assert.DoesNotContain("method=\"post\"", view);
        Assert.DoesNotContain("method=\"POST\"", view);
        Assert.DoesNotContain("asp-antiforgery", view);
        Assert.DoesNotContain(">Delete<", view);
        Assert.DoesNotContain(">Edit<", view);
        Assert.DoesNotContain(">Amend<", view);
        Assert.DoesNotContain("NewValues", view);
        Assert.DoesNotContain("OldValues", view);
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
