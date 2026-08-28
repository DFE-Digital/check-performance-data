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

    [Fact]
    public void An_empty_reference_document_is_explained_rather_than_blamed_on_the_user()
    {
        // QualificationReferenceBlobClient degrades a missing blob to an empty lookup. Without this
        // branch the user got two empty dropdowns and an endless "Select the Awarding Organisation
        // (AO) name" error for a problem they cannot fix. Mirrors ResultSearch's no-results state.
        var view = ViewSource();

        Assert.Contains("Model.AwardingOrganisations.Any()", view);
        Assert.Contains("We cannot list qualifications at the moment", view);
        Assert.Contains("asp-controller=\"ResultIssue\"", view);
    }

    [Fact]
    public void The_change_link_context_survives_the_form_round_trip()
    {
        // Arriving from the summary's Change link and posting the same qualification must return to
        // the summary; the flag has to be carried by the form to get there.
        var view = ViewSource();

        Assert.Contains("name=\"fromSummary\"", view);
        Assert.Contains("Model.FromSummary", view);
        Assert.Contains("asp-action=\"Summary\"", view);
    }

    [Fact]
    public void The_headings_come_from_the_flow_config_not_hardcoded_copy()
    {
        // The title, pageTitle and validationFailure on the select-qualification page were dead
        // config: MissingQualificationFlowTests pinned them while the view and controller ignored
        // them, so a content edit changed nothing on screen and no test noticed.
        var view = ViewSource();

        Assert.Contains("Model.Page.PageTitle", view);
        Assert.Contains("Model.ResolvedTitle", view);
        Assert.DoesNotContain(
            "<h1 class=\"govuk-heading-xl\">Provide the missing qualification details for @Model.PupilName</h1>",
            view);
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
