using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#297848: pins the details page's markup — question-partial dispatch, the syllabus gap message,
// and the summary card's row order (the Figma mock swapped AO and QAN, and this pin stops that
// swap being reproduced here).
public sealed class QualificationDetailsViewSourceTests
{
    private static string QuestionPartial()
        => File.ReadAllText(Path.Combine(
            RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Journey", "_Question.cshtml"));

    private static string SyllabusPartial()
        => File.ReadAllText(Path.Combine(
            RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Journey", "_SyllabusSelect.cshtml"));

    private static string DetailsView()
        => File.ReadAllText(Path.Combine(
            RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Journey", "QualificationDetails.cshtml"));

    [Fact]
    public void The_question_partial_dispatches_SyllabusSelect()
        => Assert.Contains("_SyllabusSelect", QuestionPartial());

    [Fact]
    public void The_syllabus_partial_explains_an_empty_reference()
        => Assert.Contains("We cannot list syllabus codes for this qualification yet", SyllabusPartial());

    [Fact]
    public void The_details_card_names_the_awarding_organisation_row_before_the_qan_row()
    {
        // The Figma mock swapped these two values; this pin stops the swap being reproduced.
        var view = DetailsView();

        var aoIndex = view.IndexOf("Awarding Organisation name", StringComparison.Ordinal);
        var qanIndex = view.IndexOf("Qualification number", StringComparison.Ordinal);

        Assert.True(aoIndex >= 0);
        Assert.True(qanIndex >= 0);
        Assert.True(aoIndex < qanIndex);
    }

    [Fact]
    public void There_is_no_late_results_reminder_on_the_missing_qualification_details_page()
        // The "check the second late results" inset is incorrect-grade-specific; this journey's
        // qualification is entirely absent from the data, so no late-results file could contain it.
        => Assert.DoesNotContain("second late results", DetailsView());

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
