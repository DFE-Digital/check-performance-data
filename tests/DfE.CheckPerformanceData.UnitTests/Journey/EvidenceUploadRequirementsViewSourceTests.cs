namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// #514 (AB#304138): the evidence requirements were behind a "What evidence do I need?"
// details component, so a school could upload and submit without ever opening it. The
// requirements now show by default under a heading. Same hostless view-source pattern as
// EvidenceUploadDuplicateWarningViewSourceTests: read the .cshtml as text.
public sealed class EvidenceUploadRequirementsViewSourceTests
{
    [Fact]
    public void EvidenceRequirements_AreNotHiddenInADetailsComponent()
    {
        var view = ReadEvidenceUploadView();

        Assert.DoesNotContain("govuk-details", view);
        Assert.DoesNotContain("<details", view);
        Assert.DoesNotContain("<summary", view);
    }

    [Fact]
    public void EvidenceRequirements_ShowUnderAHeading_BeforeTheForm()
    {
        var view = ReadEvidenceUploadView();

        const string heading =
            "<h2 class=\"govuk-heading-m\">@(Model.Page.DetailsSummary ?? \"What evidence do I need?\")</h2>";
        Assert.Contains(heading, view);

        var headingAt = view.IndexOf(heading, StringComparison.Ordinal);
        var contentAt = view.IndexOf("key = Model.ContentKey+\"-evidence\"", StringComparison.Ordinal);
        var formAt = view.IndexOf("<form", StringComparison.Ordinal);
        Assert.True(headingAt < contentAt, "The requirements must follow their heading.");
        Assert.True(contentAt < formAt, "The requirements must show before the upload form.");
    }

    private static string ReadEvidenceUploadView()
    {
        // Repo root is three levels up from tests/DfE.CheckPerformanceData.UnitTests/Journey/.
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));
        return File.ReadAllText(Path.Combine(
            repoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Journey", "EvidenceUpload.cshtml"));
    }

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => path;
}
