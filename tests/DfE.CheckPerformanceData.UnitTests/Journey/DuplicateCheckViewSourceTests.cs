using System.Runtime.CompilerServices;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Common;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

/// <summary>
/// Pins the duplicate-check page's conflict-render surface (AB#297780) to the same GDS pattern the
/// PupilSearch page already uses (PupilSearchViewSourceTests): an attention banner gated on
/// <c>ConflictAttentionHtml</c>, a <c>govuk-error-summary</c> gated on an invalid ModelState, a
/// field-level error at <c>selectedPupilId</c>, and wording from the shared DuplicateRequestMessages
/// constants so the pupil-search and duplicate-check conflicts can never drift apart (SC-003).
/// </summary>
public sealed class DuplicateCheckViewSourceTests
{
    private static string ViewSource() =>
        File.ReadAllText(Path.Combine(
            RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Journey", "DuplicateCheck.cshtml"));

    [Fact]
    public void The_attention_banner_is_gated_on_conflict_state()
    {
        var view = ViewSource();

        Assert.Contains("Model.ConflictAttentionHtml is not null", view);
        Assert.Contains("role=\"region\"", view);
        Assert.Contains("moj-alert--warning moj-alert--with-heading", view);
        Assert.Contains("@Html.Raw(Model.ConflictAttentionHtml)", view);
    }

    [Fact]
    public void The_attention_banner_renders_reference_link_and_pupil_name()
    {
        var view = ViewSource();

        Assert.Contains("Model.ConflictErrorReference", view);
        Assert.Contains("Model.ConflictErrorLink", view);
        Assert.Contains("Model.ConflictPupilName", view);
        Assert.Contains("View submitted request (opens in new tab)", view);
    }

    [Fact]
    public void The_error_summary_is_gated_on_an_invalid_model_state()
    {
        var view = ViewSource();

        Assert.Contains("!ViewData.ModelState.IsValid", view);
        Assert.Contains("ViewData.ModelState[string.Empty]", view);
        Assert.Contains("ViewData.ModelState[\"selectedPupilId\"]", view);
        Assert.Contains("#selectedPupilId", view);
        Assert.Contains("govuk-error-message", view);
    }

    [Fact]
    public void The_match_list_is_rendered_unconditionally()
    {
        // FR-006: a blocked hand-off keeps the match list visible so the school can still act.
        var view = ViewSource();

        Assert.Contains("Model.Matches", view);
        Assert.Contains("SwitchToInclude", view);
        Assert.Contains("IncludeThisPupil", view);
    }

    [Fact]
    public void Conflict_wording_comes_from_DuplicateRequestMessages()
    {
        var noun = LearnerNoun.For(CheckingWindowType.KS4June);
        Assert.Equal("Choose another pupil", DuplicateRequestMessages.FieldErrorMessage(noun));
        Assert.Equal("A request has already been submitted for this pupil",
            DuplicateRequestMessages.ErrorSummaryMessage(noun));

        var banner = DuplicateRequestMessages.AttentionBannerHtml(
            isSelf: false, reasonsMatch: false, "Remove", "John Doe", "REF001",
            "/11111111-1111-1111-1111-111111111111/AmendmentRequests/REF001/view", "A Colleague", noun);
        Assert.Contains("View submitted request (opens in new tab)", banner);
        Assert.Contains("/AmendmentRequests/REF001/view", banner);
        Assert.Contains("A Colleague", banner);
    }

    [Fact]
    public void Conflict_markup_matches_the_pupil_search_page()
    {
        // SC-003: the duplicate-check conflict must be byte-identical in structure/wording to the
        // PupilSearch conflict — both share DuplicateRequestMessages constants and the same GDS
        // banner/error-summary guard pattern. Any fragment both pages must carry is asserted on
        // BOTH view sources so a drift in either fails here.
        var duplicate = ViewSource();
        var pupilSearch = File.ReadAllText(Path.Combine(
            RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Journey", "PupilSearch.cshtml"));

        foreach (var fragment in new[]
        {
            "Model.ConflictAttentionHtml is not null",
            "moj-alert--warning",
            "aria-label=\"warning: There is a problem\"",
            "@Html.Raw(Model.ConflictAttentionHtml)",
            "ViewData.ModelState[string.Empty]",
            "ViewData.ModelState[\"selectedPupilId\"]",
            "govuk-error-summary",
        })
        {
            Assert.Contains(fragment, duplicate);
            Assert.Contains(fragment, pupilSearch);
        }
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