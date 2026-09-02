using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.AmendmentRequests;

public sealed class EstablishmentAmendmentRequestsViewSourceTests
{
    [Fact]
    public void Index_renders_status_presentation_from_the_view_model()
    {
        string source = ViewSource("Index.cshtml");

        Assert.Contains("row.TagClass", source);
        Assert.Contains("row.TagLabel", source);
        Assert.DoesNotContain("row.Status.ToString()", source);
    }

    [Fact]
    public void Details_back_link_returns_to_the_establishment_summary()
    {
        string source = ViewSource("UrnAmendmentView.cshtml");

        Assert.Contains("Url.Action(\"Index\", \"EstablishmentAmendmentRequests\")", source);
        Assert.DoesNotContain("Url.Action(\"Index\", \"AmendmentRequests\"", source);
    }

    [Fact]
    public void Details_view_renders_the_learner_noun_not_a_hardcoded_pupil()
    {
        // #359 made the noun dynamic (student on 16-19, pupil elsewhere); #358's new details view
        // predates that and hardcoded "pupil", so a 16-19 request showed mixed wording on one page.
        // Mirrors Views/SubmittedRequest/View.cshtml:38,55 exactly.
        string source = ViewSource("UrnAmendmentView.cshtml");

        Assert.Contains("What @Model.LearnerNoun.Singular data would you like to change?", source);
        Assert.Contains("@Model.LearnerNoun.SingularCapitalised name", source);
        Assert.DoesNotContain("What pupil data", source);
        Assert.DoesNotContain(">Pupil name<", source);
    }

    private static string ViewSource(string fileName) =>
        File.ReadAllText(Path.Combine(
            RepoRoot,
            "src", "DfE.CheckPerformanceData.Web", "Views", "AmendmentRequests",
            "EstablishmentAmendmentRequests", fileName));

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
