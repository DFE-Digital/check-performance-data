namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#296081: the selection-time duplicate warning is wired across three files that can
// silently drift apart — the data attribute in _FileUpload.cshtml, the script itself,
// and its registration in _Layout. This pins all three, plus the exact ticket wording,
// as source-level contracts (house pattern: view-source tests, see LayoutRenderTests).
public sealed class EvidenceUploadDuplicateWarningViewSourceTests
{
    private const string TicketWording =
        "The file name has already been used. Upload a file with a different name.";

    [Fact]
    public void FileUploadPartial_RendersExistingFileNamesDataAttribute()
    {
        var source = ReadWebFile(@"Views\Journey\_FileUpload.cshtml");
        Assert.Contains("data-existing-file-names", source);
        Assert.Contains("JsonSerializer.Serialize(Model.ExistingFileNames)", source);
    }

    [Fact]
    public void Layout_RegistersEvidenceUploadValidationScript()
    {
        var source = ReadWebFile(@"Views\Shared\_Layout.cshtml");
        Assert.Contains("evidence-upload-validation.js", source);
    }

    [Fact]
    public void Script_UsesTicketWording_AndNeverDisablesTheUploadPath()
    {
        var source = ReadWebFile(@"wwwroot\js\evidence-upload-validation.js");
        Assert.Contains(TicketWording, source);
        // Courtesy warning only: the script must not block the server round-trip.
        Assert.DoesNotContain("disabled", source);
        Assert.DoesNotContain("preventDefault", source);
    }

    [Fact]
    public void ServerAndClient_ShareTheExactTicketWording()
    {
        var service = ReadSrcFile(@"DfE.CheckPerformanceData.Application\Journey\JourneyValidationService.cs");
        Assert.Contains(TicketWording, service);
    }

    // ReadWebFile / ReadSrcFile: build on the LayoutRenderTests path-resolution helper,
    // rooted at src\DfE.CheckPerformanceData.Web\ and src\ respectively.

    private static string ReadWebFile(string relativePath) =>
        File.ReadAllText(Path.Combine(SrcRoot(), "DfE.CheckPerformanceData.Web", relativePath));

    private static string ReadSrcFile(string relativePath) =>
        File.ReadAllText(Path.Combine(SrcRoot(), relativePath));

    private static string SrcRoot()
    {
        // Use the .cs file's path via CallerFilePath rather than AppContext.BaseDirectory
        // so the test works regardless of where the test binary is dropped (in-tree
        // bin/Debug/... or out-of-tree via `dotnet test -o ...`). That .cs file lives at
        // {repo}/tests/DfE.CheckPerformanceData.UnitTests/Journey/EvidenceUploadDuplicateWarningViewSourceTests.cs,
        // so the repo root is three levels up; src/ sits directly under it.
        var thisFile = ThisFilePath();
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
        return Path.Combine(repoRoot, "src");
    }

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => path;
}
