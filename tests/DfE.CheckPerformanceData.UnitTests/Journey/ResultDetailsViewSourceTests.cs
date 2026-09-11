using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#301903: pins the "Incorrect grade details" markup. The page must describe the qualification
// from the 16-19 reference and show its awarding organisation — and stay usable (name from the
// results file, no AO row) when the QAN is not in the reference. Source-file assertions, because
// the always-on CI gate runs no E2E and nothing else notices a dropped row.
public sealed class ResultDetailsViewSourceTests
{
    private static string ViewSource() =>
        File.ReadAllText(Path.Combine(
            RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Journey", "ResultDetails.cshtml"));

    [Fact]
    public void The_qualification_name_row_reads_the_reference_backed_name()
    {
        var view = ViewSource();

        Assert.Contains("@Model.SelectedResultQualificationName", view);
        Assert.DoesNotContain("@Model.SelectedResult.QualificationName", view);
    }

    [Fact]
    public void The_awarding_organisation_row_renders_only_when_the_qualification_resolved()
    {
        var view = ViewSource();
        var rowStart = view.IndexOf("Awarding Organisation name", StringComparison.Ordinal);

        Assert.True(rowStart > 0, "no Awarding Organisation row");
        var guardStart = view.LastIndexOf("@if (Model.SelectedResultQualification is not null)", rowStart, StringComparison.Ordinal);
        Assert.True(guardStart > 0, "the AO row is not guarded on a resolved qualification");
        Assert.Contains("@Model.SelectedResultQualification.AwardingOrganisation", view);
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
