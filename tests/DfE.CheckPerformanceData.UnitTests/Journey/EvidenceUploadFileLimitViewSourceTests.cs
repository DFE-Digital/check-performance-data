using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#304900: two pieces of markup carry the acceptance criteria and nothing else in the build
// notices if either is dropped (E2E is off the always-on gate). Source-level contracts, the
// house pattern — see EvidenceUploadDuplicateWarningViewSourceTests.
public sealed class EvidenceUploadFileLimitViewSourceTests
{
    [Fact]
    public void FileUploadPartial_StatesTheLimitInTheFilesAddedLine()
    {
        // The school is told the limit before it reaches it, from the model — never a literal 6
        // in the view, which could drift from the rule.
        var source = ReadWebFile("Views", "Journey", "_FileUpload.cshtml");

        Assert.Contains("@Model.UploadedFiles.Count of @Model.MaxEvidenceFiles files added.", source);
    }

    [Fact]
    public void ErrorSummary_LinksAnUploadErrorToTheFileInput()
    {
        // "User is told why the file was rejected": the rejection is an upload error, and an
        // upload error must appear in the error summary as a link to the file input.
        var source = ReadWebFile("Views", "Journey", "_JourneyErrorSummary.cshtml");

        Assert.Contains("<govuk-error-summary-item href=\"#fileUpload\">@Model.UploadError</govuk-error-summary-item>", source);
    }

    // Path.Combine with separate segments, never an embedded backslash: a backslash is a legal
    // file-name character on the Linux runner, so "Views\Journey" would not be found there.
    private static string ReadWebFile(params string[] relativeSegments) =>
        File.ReadAllText(Path.Combine([SrcRoot(), "DfE.CheckPerformanceData.Web", .. relativeSegments]));

    // This file is {repo}/tests/DfE.CheckPerformanceData.UnitTests/Journey/<this>.cs, so the
    // repo root is three directories up and src/ sits directly under it.
    private static string SrcRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "..", "src"));
}
