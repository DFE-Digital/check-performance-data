using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Static Razor-source assertions for Views/CheckYourPupilData/Index.cshtml, in the same hostless
// style as LayoutRenderTests. #317 gave the page three form states — none, one, many — and these
// pin the shape of each, so an accidental branch deletion or a slide back to the window-type test
// fails here rather than in E2E.
public sealed class CheckYourPupilDataViewRenderTests
{
    private static string ReadView()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));
        return File.ReadAllText(Path.Combine(repoRoot,
            "src", "DfE.CheckPerformanceData.Web", "Views", "CheckYourPupilData", "Index.cshtml"));
    }

    private static string ThisFilePath([CallerFilePath] string path = "") => path;

    [Fact]
    public void The_form_is_driven_by_the_available_options_not_by_the_window_being_open()
    {
        var view = ReadView();

        // AB#298317: four states — enquiry-only question, one other option, many, none.
        Assert.Contains("Model.OffersEnquiryOnly", view);
        Assert.Contains("Model.AvailableNextSteps.Count == 1", view);
        Assert.Contains("Model.AvailableNextSteps.Count > 1", view);
        // The outer window's open/closed state no longer gates the form: a window can be open with
        // every exercise inside it shut.
        Assert.DoesNotContain("Model.IsWindowOpen", view);
    }

    [Fact]
    public void No_available_option_renders_no_form_at_all()
    {
        // AB#298317: the closed statement moved up into the intro paragraph (see
        // The_closed_window_paragraph_…), so nothing renders below the tables when nothing is open.
        var view = ReadView();

        Assert.DoesNotContain("closed for changes", view);
        // Every form is inside one of the three option branches; none is unconditional.
        var firstForm = view.IndexOf("<form", StringComparison.Ordinal);
        var firstBranch = view.IndexOf("@if (Model.OffersEnquiryOnly)", StringComparison.Ordinal);
        Assert.True(firstBranch >= 0 && firstForm > firstBranch, "no form may render before the option branches");
    }

    [Fact]
    public void A_single_available_option_renders_a_button_not_a_one_item_radio_group()
    {
        var view = ReadView();
        var single = Section(view, "Model.AvailableNextSteps.Count == 1", "Model.AvailableNextSteps.Count > 1");

        Assert.Contains("<form", single);
        Assert.DoesNotContain("govuk-radios", single);
        Assert.Contains("govuk-button", single);
        // The choice still has to reach the POST, which re-derives and re-checks it.
        Assert.Contains("type=\"hidden\"", single);
        Assert.Contains(nameof(DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData
            .CheckYourPupilDataViewModel.SelectedNextStep), single);
    }

    [Fact]
    public void Confirm_reads_last_after_the_or_divider()
    {
        // "Confirm ... data is correct" is the alternative to doing anything, so it keeps the GDS
        // divider and the last position it has always had — the exercise sort order drives the rest.
        var view = ReadView();

        var divider = view.IndexOf("govuk-radios-divider", StringComparison.Ordinal);
        var confirmItem = view.IndexOf("NextSteps.Confirm, Model.LearnerNoun)</govuk-radios-item>",
            StringComparison.Ordinal);

        Assert.True(divider > 0 && confirmItem > divider,
            "the Confirm option must render after the 'or' divider");
        Assert.Contains("Where(s => s != NextSteps.Confirm)", view);
    }

    [Fact]
    public void The_deadline_sentence_uses_the_pupil_data_exercise_not_the_window()
    {
        var view = ReadView();

        Assert.Contains("Model.PupilDataEndDate", view);
        Assert.Contains("Model.IsPupilDataOpen", view);
        // The outer window's end date must not reappear here: on a multi-exercise window it is
        // months after the real pupil-data deadline.
        Assert.DoesNotContain("Model.WindowEndDate", view);
        Assert.DoesNotContain("Model.WindowEndTime", view);
    }

    [Fact]
    public void The_closed_window_paragraph_is_scoped_to_pupil_data_being_closed()
    {
        // AB#298317: once pupil data shuts the page says so, names the next opportunity when the
        // admin has set one, and says what the school can still do — inside one branch, so the
        // open-state deadline sentence and the closed paragraph can never both print.
        var view = ReadView();
        Assert.Contains("You must request any changes to @Model.LearnerNoun.Singular data before", view);

        var closed = Section(view, "@if (Model.PupilDataEndDate is not null && !Model.IsPupilDataOpen)", "<a asp-action=\"DownloadAll\"");
        Assert.Contains("data checking window has closed.", closed);
        Assert.Contains("@if (Model.NextOpportunity is not null)", closed);
        Assert.Contains("The next opportunity to review your performance data will be in @Model.NextOpportunity.", closed);
        Assert.Contains("@if (Model.IsResultsEnquiryOpen)", closed);
        Assert.Contains("You can still view your exam results and report any issues.", closed);
        Assert.Contains("You can still view and download your @Model.LearnerNoun.Singular data.", closed);
        // The old past-tense deadline sentence is gone: the paragraph above replaces it.
        Assert.DoesNotContain("closed at", view);
    }

    [Fact]
    public void The_enquiry_only_state_asks_the_yes_no_question()
    {
        var view = ReadView();
        var enquiryOnly = Section(view, "@if (Model.OffersEnquiryOnly)", "Model.AvailableNextSteps.Count == 1");

        Assert.Contains("<form", enquiryOnly);
        Assert.Contains("govuk-radios", enquiryOnly);
        Assert.Contains("Would you like to report an issue with an exam result?", enquiryOnly);
        Assert.Contains("value=\"@NextSteps.ResultsEnquiry\">Yes<", enquiryOnly);
        Assert.Contains("value=\"@NextSteps.SignOut\">No, I'd like to sign out of this service<", enquiryOnly);
        // Not the page heading — the EditableTitle above is the only h1. The attribute form (not
        // bare prose) is checked, because the branch's own comment explains this in words.
        Assert.DoesNotContain("is-page-heading=", enquiryOnly);
        // The amendment options never leak into this state.
        Assert.DoesNotContain("NextSteps.RequestChange", enquiryOnly);
        Assert.DoesNotContain("NextSteps.Confirm", enquiryOnly);
    }

    [Fact]
    public void The_page_still_has_exactly_one_h1()
    {
        // The EditableTitle component renders the only heading; the radios legend must stay a
        // legend. A hand-written heading tag would be written with a class, so that is what is
        // banned here — the phrase also appears in an explanatory comment, which is harmless.
        var view = ReadView();

        Assert.DoesNotContain("<h1 class", view);
        Assert.Contains("headingLevel = \"h1\"", view);
        Assert.DoesNotContain("is-page-heading=", view);
    }

    private static string Section(string view, string fromMarker, string toMarker)
    {
        var start = view.IndexOf(fromMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"marker not found: {fromMarker}");
        var end = view.IndexOf(toMarker, start + fromMarker.Length, StringComparison.Ordinal);
        return end < 0 ? view[start..] : view[start..end];
    }
}
