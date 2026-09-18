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

    // Nit: every banner this page shows is neutral (abandoned, already transferred, refused) —
    // role="alert" is reserved for a success banner GOV.UK doesn't have here.
    [Fact]
    public void Pull_page_banner_is_a_neutral_region_not_an_alert()
    {
        var view = View("Index.cshtml");
        Assert.Contains("role=\"region\"", view);
        Assert.DoesNotContain("role=\"alert\"", view);
    }

    // Nit: WindowId's aria-describedby must not carry a trailing space when there is no error.
    // Review finding (18 Sep): warn about a missing egress storage account on the first screen
    // and on the confirm screen, not only in the failure banner after Confirm.
    [Fact]
    public void Pull_page_warns_when_egress_storage_is_not_configured()
    {
        var view = View("Index.cshtml");
        Assert.Contains("@if (Model.StorageNotConfigured)", view);
        Assert.Contains("data-testid=\"egress-storage-not-configured\"", view);
        Assert.Contains("ConnectionStrings:EgressStorage", view);
    }

    [Fact]
    public void Summary_warns_when_egress_storage_is_not_configured()
    {
        var view = View("Summary.cshtml");
        Assert.Contains("@if (Model.StorageNotConfigured)", view);
        Assert.Contains("data-testid=\"egress-storage-not-configured\"", view);
    }

    [Fact]
    public void Window_select_aria_describedby_has_no_trailing_space_without_an_error()
    {
        var view = View("Index.cshtml");
        Assert.Contains("aria-describedby=\"WindowId-hint@(Model.WindowError is not null ? \" WindowId-error\" : \"\")\"", view);
    }

    // Nit: failed stages must get the red tag; every other stage keeps blue.
    [Fact]
    public void Saved_runs_give_failed_stages_the_red_tag()
    {
        var view = View("Index.cshtml");
        Assert.Contains("<strong class=\"govuk-tag @StageTagClass(run.Status)\">", view);
        Assert.Contains("govuk-tag--red", view);
        Assert.Contains("EgressRunStatus.PreprocessingFailed or EgressRunStatus.TransferFailed => \"govuk-tag--red\"", view);
    }

    // S5: the output-types error must sit inside the form group between hint and checkboxes, be
    // referenced by the fieldset's aria-describedby, and the group must carry the error class —
    // fallout from the Task 12 duplicate-summary workaround having moved the error outside entirely.
    [Fact]
    public void Pull_page_associates_the_output_types_error_with_its_fieldset()
    {
        var view = View("Index.cshtml");
        Assert.Contains("govuk-form-group @(Model.OutputTypesError is not null ? \"govuk-form-group--error\" : \"\")", view);
        // The library appends its own auto-generated OutputTypes-hint id to whatever value is
        // supplied here (verified live), so only the error id needs adding.
        Assert.Contains("<govuk-checkboxes-fieldset aria-describedby=\"@(Model.OutputTypesError is not null ? \"OutputTypes-error\" : \"\")\">", view);
        var hintIndex = view.IndexOf("<govuk-checkboxes-hint>", StringComparison.Ordinal);
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

    // Nit: rewritten from developer-note phrasing ("discarded by the preprocessing filter, not
    // here") into plain user-facing language.
    [Fact]
    public void Results_page_explains_filtering_in_plain_language()
    {
        var view = View("Results.cshtml");
        Assert.DoesNotContain("discarded by the preprocessing filter", view);
        Assert.Contains("Only approved and auto-approved requests will be included", view);
    }

    // Nit: every page using _RunHeader has an <h1 class="govuk-heading-xl">, so its caption must
    // be govuk-caption-xl, matched to that heading size.
    [Fact]
    public void RunHeader_caption_size_matches_the_pages_heading()
    {
        var view = View("_RunHeader.cshtml");
        Assert.Contains("govuk-caption-xl", view);
        Assert.DoesNotContain("govuk-caption-l\"", view);
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
        Assert.Contains("@if (offerConfirm)", view);
    }

    // Nit: a Transferring run already has a transfer in flight; Summary must not offer Confirm
    // for it either.
    [Fact]
    public void Summary_does_not_offer_confirm_while_a_transfer_is_already_in_flight()
    {
        var view = View("Summary.cshtml");
        Assert.Contains("Model.Run.Status != EgressRunStatus.Transferring", view);
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
