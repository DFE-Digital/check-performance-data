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
        var contentAt = view.IndexOf("key = block.Key+\"-evidence\"", StringComparison.Ordinal);
        var formAt = view.IndexOf("<form", StringComparison.Ordinal);
        Assert.True(headingAt < contentAt, "The requirements must follow their heading.");
        Assert.True(contentAt < formAt, "The requirements must show before the upload form.");
    }

    [Fact]
    public void EvidenceRequirements_ShowOneBlockForEachKey_UnderOneHeading()
    {
        // #591: a school may tick several options, and each has its own requirements.
        var view = ReadEvidenceUploadView();

        Assert.Contains("@foreach (var block in Model.EvidenceContentBlocks)", view);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(view, "<h2 "));
        Assert.DoesNotContain("Model.ContentKey", view);
    }

    [Fact]
    public void AnOptionsBlock_SeedsWithAPlaceholderThatNamesTheOption()
    {
        // #591: three blocks with the same placeholder give a guidance writer no way to tell
        // which option each one is for.
        var view = ReadEvidenceUploadView();

        Assert.Contains("PLACEHOLDER EVIDENCE REQUIREMENTS COPY FOR: {Html.Encode(block.OptionLabel)}", view);
    }

    [Fact]
    public void ASectionsSharedBlock_SeedsWithAPlaceholderThatNamesTheSection()
    {
        // #633: the shared block shows beside the option blocks, so it needs its own name too.
        var view = ReadEvidenceUploadView();

        Assert.Contains("PLACEHOLDER GENERAL EVIDENCE REQUIREMENTS COPY FOR: {Html.Encode(block.SectionLabel)}", view);
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
