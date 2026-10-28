namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// #711: the Include journey's two decision pages are shared between key stages. They must take
// the noun from the model ("student" on 16-19) and offer "Start adding" only when the window type
// has an Add flow — 16-19 has none, so the button would only bounce to Check your data.
public sealed class IncludeDecisionViewSourceTests
{
    private static string ReadViewSource(string relativePath)
    {
        var viewsDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "DfE.CheckPerformanceData.Web"));
        return File.ReadAllText(Path.Combine(viewsDir, relativePath));
    }

    [Theory]
    [InlineData("Views/Journey/PupilNotFound.cshtml")]
    [InlineData("Views/Journey/AlreadyIncluded.cshtml")]
    public void The_page_wraps_the_add_form_in_CanAdd(string view)
    {
        var source = ReadViewSource(view);

        Assert.Contains("@if (Model.CanAdd)", source);
        Assert.Contains("StartAddingPupil", source);
    }

    [Theory]
    [InlineData("Views/Journey/PupilNotFound.cshtml")]
    [InlineData("Views/Journey/AlreadyIncluded.cshtml")]
    public void The_page_takes_its_noun_from_the_model_and_never_says_pupil(string view)
    {
        var source = ReadViewSource(view);

        Assert.Contains("Model.LearnerNoun.", source);
        // The rendered sentences. Code may still say "pupil" (the StartAddingPupil action, the
        // "select-pupil" page id, the model type), so the check names the old prose, not the word.
        foreach (var oldProse in new[]
                 {
                     "Pupil not found", "No pupil", "the pupil", "this pupil", "This pupil", "new pupil",
                     "Matching pupils", "Pupils matching", "non-included pupils", "a pupil matching"
                 })
        {
            Assert.DoesNotContain(oldProse, source);
        }
    }

    [Theory]
    [InlineData("Views/Journey/PupilNotFound.cshtml")]
    [InlineData("Views/Journey/AlreadyIncluded.cshtml")]
    public void The_title_and_the_heading_are_the_same_words(string view)
    {
        var source = ReadViewSource(view);

        // docs/accessibility.md: ViewData["Title"] carries the same words as the one <h1>. The
        // title is a C# interpolated string and the heading is Razor, so the noun appears as
        // {Model.LearnerNoun.X} in one and @Model.LearnerNoun.X in the other.
        var title = System.Text.RegularExpressions.Regex.Match(source, @"ViewBag\.Title\s*=\s*(\$?"".*?"");").Groups[1].Value
            .Trim('$', '"')
            .Replace("{Model.LearnerNoun.SingularCapitalised}", "@Model.LearnerNoun.SingularCapitalised")
            .Replace("{Model.LearnerNoun.Singular}", "@Model.LearnerNoun.Singular");
        var heading = System.Text.RegularExpressions.Regex.Match(source, @"<h1[^>]*>(.*?)</h1>").Groups[1].Value.Trim();
        Assert.False(string.IsNullOrEmpty(title));
        Assert.Equal(title, heading);
    }
}
