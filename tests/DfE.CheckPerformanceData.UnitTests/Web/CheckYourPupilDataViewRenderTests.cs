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

        Assert.Contains("Model.AvailableNextSteps.Count == 0", view);
        Assert.Contains("Model.AvailableNextSteps.Count == 1", view);
        // The outer window's open/closed state no longer gates the form: a window can be open with
        // every exercise inside it shut.
        Assert.DoesNotContain("Model.IsWindowOpen", view);
    }

    [Fact]
    public void No_available_option_renders_no_form_at_all()
    {
        var view = ReadView();
        var noOptions = Section(view, "Model.AvailableNextSteps.Count == 0", "Model.AvailableNextSteps.Count == 1");

        Assert.DoesNotContain("<form", noOptions);
        Assert.DoesNotContain("govuk-radios", noOptions);
        // The tables, the search and the downloads all sit above this block and are untouched, so a
        // closed window still says what it is rather than showing an empty page.
        Assert.Contains("closed for changes", noOptions);
    }

    [Fact]
    public void A_single_available_option_renders_a_button_not_a_one_item_radio_group()
    {
        var view = ReadView();
        var single = Section(view, "Model.AvailableNextSteps.Count == 1", "else\n{");

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
    public void The_deadline_sentence_has_a_past_tense_variant()
    {
        var view = ReadView();

        // The noun is the window's, so the sentence is asserted around it rather than through it.
        Assert.Contains("You must request any changes to @Model.LearnerNoun.Singular data before", view);
        Assert.Contains("closed at", view);
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
