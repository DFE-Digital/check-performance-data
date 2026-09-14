using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// Pins the contracts the E2E tier cannot guard on the always-on gate (AB#294553): every screen
// works without JavaScript, the progress page is an enhancement over a real form, tabs are one
// per output type, and the target container is stated but never editable.
public sealed class EgressViewSourceTests
{
    private static string View(string name) => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Egress", name));

    private static string Script() => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "DfE.CheckPerformanceData.Web", "wwwroot", "js", "egress-preprocess.js"));

    [Fact]
    public void The_egress_views_render_under_the_admin_layout()
        => Assert.Contains("Layout = \"_AdminLayout\";", View("_ViewStart.cshtml"));

    [Fact]
    public void Pull_page_is_a_plain_form_with_a_window_select_and_output_type_checkboxes()
    {
        var view = View("Index.cshtml");
        Assert.Contains("<form method=\"post\" action=\"/admin/egress\"", view);
        Assert.Contains("@Html.AntiForgeryToken()", view);
        Assert.Contains("<select class=\"govuk-select", view);
        Assert.Contains("name=\"WindowId\"", view);
        Assert.Contains("<govuk-checkboxes name=\"OutputTypes\">", view);
        Assert.Contains("Pull data from Zendesk", view);
        Assert.Contains("data-testid=\"egress-saved-runs\"", view);
        Assert.Contains("data-testid=\"egress-completed-runs\"", view);
    }

    [Fact]
    public void Pull_page_lists_refusals_in_an_error_summary()
    {
        var view = View("Index.cshtml");
        Assert.Contains("@foreach (var refusal in Model.Refusals)", view);
        Assert.Contains("govuk-error-summary", view);
    }

    // S5: the output-types error must sit inside the form group between hint and checkboxes, be
    // referenced by the fieldset's aria-describedby, and the group must carry the error class —
    // fallout from the Task 12 duplicate-summary workaround having moved the error outside entirely.
    [Fact]
    public void Pull_page_associates_the_output_types_error_with_its_fieldset()
    {
        var view = View("Index.cshtml");
        Assert.Contains("govuk-form-group @(Model.OutputTypesError is not null ? \"govuk-form-group--error\" : \"\")", view);
        Assert.Contains("<govuk-checkboxes-fieldset aria-describedby=\"OutputTypes-hint@(Model.OutputTypesError is not null ? \" OutputTypes-error\" : \"\")\">", view);
        var hintIndex = view.IndexOf("<govuk-checkboxes-hint id=\"OutputTypes-hint\">", StringComparison.Ordinal);
        var errorIndex = view.IndexOf("id=\"OutputTypes-error\"", StringComparison.Ordinal);
        var itemsIndex = view.IndexOf("<govuk-checkboxes-item", StringComparison.Ordinal);
        Assert.True(hintIndex >= 0 && errorIndex > hintIndex && itemsIndex > errorIndex,
            "the error message must render between the hint and the checkbox items");
        Assert.Contains("<govuk-checkboxes-before-inputs>", view);
    }

    [Fact]
    public void Results_page_has_one_tab_per_output_type_and_offers_save_and_abandon()
    {
        var view = View("Results.cshtml");
        Assert.Contains("<govuk-tabs>", view);
        Assert.Contains("@foreach (var output in Model.Run.Outputs)", view);
        Assert.Contains("id=\"@output.OutputType.ToString().ToLowerInvariant()\"", view);
        Assert.Contains("Proceed to preprocessing", view);
        Assert.Contains("Save and exit", view);
        Assert.Contains("Abandon run", view);
        Assert.Contains("RunPageViewModel.RawColumns", view);
    }

    // M2: a PreprocessingFailed run released its pair — Failed.cshtml's own copy says "start a new
    // run" — so Results must not offer a path back into preprocessing for it.
    [Fact]
    public void Results_page_hides_proceed_to_preprocessing_for_a_preprocessing_failed_run()
    {
        var view = View("Results.cshtml");
        Assert.Contains("Model.Run.Status != EgressRunStatus.PreprocessingFailed", view);
    }

    [Fact]
    public void Preprocessing_page_is_a_real_form_the_script_enhances()
    {
        var view = View("Preprocessing.cshtml");
        Assert.Contains("data-module=\"egress-preprocess\"", view);
        Assert.Contains("data-stream-url=\"@Model.StreamUrl\"", view);
        Assert.Contains("<form method=\"post\" action=\"/admin/egress/runs/@Model.Run.Id/preprocessing\"", view);
        Assert.Contains("data-egress-start", view);
        Assert.Contains("@foreach (var step in Model.StepNames)", view);
        Assert.Contains("data-egress-step", view);
        Assert.Contains("aria-live=\"polite\"", view);
        Assert.Contains("role=\"progressbar\"", view);
        Assert.Contains("egress-preprocess.js", view);
    }

    // S6: role="progressbar" only permits presentational children per the ARIA spec, so an
    // aria-live region nested inside it may never be announced. The status paragraph must be a
    // sibling of the progressbar element, not a child.
    [Fact]
    public void Preprocessing_page_keeps_the_live_region_out_of_the_progressbar()
    {
        var view = View("Preprocessing.cshtml");
        var progressbarOpen = view.IndexOf("role=\"progressbar\"", StringComparison.Ordinal);
        var progressbarClose = view.IndexOf("</div>", progressbarOpen, StringComparison.Ordinal);
        var liveRegion = view.IndexOf("aria-live=\"polite\"", StringComparison.Ordinal);
        Assert.True(liveRegion < progressbarOpen || liveRegion > progressbarClose,
            "the aria-live region must not be nested inside the role=\"progressbar\" element");
    }

    // M4: the lock has no expiry, so a run stuck in Preprocessing after a restart must be
    // releasable from this page, not only from Results/Summary.
    [Fact]
    public void Preprocessing_page_offers_abandon()
    {
        var view = View("Preprocessing.cshtml");
        Assert.Contains("<form method=\"post\" action=\"/admin/egress/runs/@Model.Run.Id/abandon\"", view);
        Assert.Contains("data-testid=\"egress-abandon\"", view);
    }

    [Fact]
    public void The_script_bows_out_without_EventSource_and_follows_the_terminal_next_url()
    {
        var script = Script();
        Assert.Contains("'EventSource' in window", script);
        Assert.Contains("addEventListener('progress'", script);
        Assert.Contains("data.nextUrl", script);
        Assert.DoesNotContain("innerHTML", script);
    }

    [Fact]
    public void Summary_states_the_fixed_target_and_links_each_file_to_a_preview()
    {
        var view = View("Summary.cshtml");
        Assert.Contains("@Model.TargetDescription", view);
        Assert.DoesNotContain("<input", view.Replace("@Html.AntiForgeryToken()", ""));   // nothing about the target is editable
        Assert.Contains("/admin/egress/runs/@Model.Run.Id/preview/@output.OutputType", view);
        Assert.Contains("/admin/egress/runs/@Model.Run.Id/download/@output.OutputType", view);
        Assert.Contains("<form method=\"post\" action=\"/admin/egress/runs/@Model.Run.Id/transfer\"", view);
        Assert.Contains("Confirm and transfer", view);
        Assert.Contains("This action cannot be undone", view);
        Assert.Contains("@if (Model.TransferError is not null)", view);
    }

    // M3: a run whose approved set is empty must not offer Confirm, only Abandon.
    [Fact]
    public void Summary_replaces_confirm_with_a_message_when_there_is_nothing_to_transfer()
    {
        var view = View("Summary.cshtml");
        Assert.Contains("Model.Run.Outputs.All(o => (o.OutputRecordCount ?? 0) == 0)", view);
        Assert.Contains("data-testid=\"egress-nothing-to-transfer\"", view);
        Assert.Contains("@if (!nothingToTransfer)", view);
    }

    [Fact]
    public void Failed_page_lists_every_failure_with_step_field_and_reason()
    {
        var view = View("Failed.cshtml");
        Assert.Contains("@foreach (var failure in Model.Run.Failures)", view);
        Assert.Contains("@failure.Step", view);
        Assert.Contains("@failure.Field", view);
        Assert.Contains("@failure.Reason", view);
        Assert.Contains("No records were transferred", view);
    }

    [Fact]
    public void Complete_page_shows_the_panel_and_the_transfer_summary()
    {
        var view = View("Complete.cshtml");
        Assert.Contains("govuk-panel--confirmation", view);
        Assert.Contains("Transfer complete", view);
        Assert.Contains("@Model.Run.TransferredByName", view);
        Assert.Contains("@Model.Run.TransferredAtUtc", view);
        Assert.Contains("Start a new egress", view);
    }

    [Fact]
    public void Every_egress_view_renders_exactly_one_h1()
    {
        foreach (var name in new[] { "Index.cshtml", "Results.cshtml", "Preprocessing.cshtml", "Failed.cshtml", "Summary.cshtml", "Preview.cshtml", "Complete.cshtml" })
        {
            var view = View(name);
            // Complete.cshtml's single heading is <h1 class="govuk-panel__title">, so counting "<h1 "
            // and "govuk-panel__title" separately double-counts that one element.
            var count = view.Split("<h1 ").Length - 1;
            Assert.True(count == 1, $"{name} renders {count} h1-level headings");
        }
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
