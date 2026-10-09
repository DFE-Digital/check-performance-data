using DfE.CheckPerformanceData.Application.ContentStaging;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentStaging;

// The editor warns anyone who opens a page that the start-up import will overwrite. The index is
// how it knows: it reads the same manifest as the importer and answers one question, "will the
// import replace this page in this environment?". A wrong answer either way is a broken promise —
// a warning on a page that is safe to edit, or silence on one whose edits will be lost.
public sealed class ImportedContentIndexTests : IDisposable
{
    private static readonly Guid Guide = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly Guid Fixture = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid Sample = Guid.Parse("22222222-0000-0000-0000-000000000003");

    private readonly string _folder = Directory.CreateTempSubdirectory("cpd-import-index-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private void Manifest(string json) => File.WriteAllText(Path.Combine(_folder, "manifest.json"), json);

    private void Bundle(string file, Guid page) =>
        File.WriteAllText(Path.Combine(_folder, file), ContentStagingJson.Serialize(new ContentBundle
        {
            ExportedAtUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            PageNodes =
            [
                new PageNodeBundleItem { Id = page, Segment = "s", Title = "t", PageType = "content" },
            ],
        }));

    private void ShippedContent()
    {
        Bundle("guide.json", Guide);
        Bundle("fixtures.json", Fixture);
        Bundle("samples.json", Sample);
        Manifest("""
            {
              "imports": [
                { "file": "guide.json", "environments": ["Development", "Production"], "existing": "replaceOlder" },
                { "file": "fixtures.json", "environments": ["Development"], "existing": "replace" },
                { "file": "samples.json", "environments": ["Development", "Production"] }
              ]
            }
            """);
    }

    [Fact]
    public void FindsAPage_ThatANewerFileWillReplace()
    {
        ShippedContent();

        var page = ImportedContentIndex.Load(_folder, "Production").Find(Guide);

        Assert.NotNull(page);
        Assert.Equal("guide.json", page.File);
        Assert.False(page.ReplacedOnEveryStart);
    }

    [Fact]
    public void FindsAPage_ThatIsReplacedOnEveryStart()
    {
        ShippedContent();

        var page = ImportedContentIndex.Load(_folder, "Development").Find(Fixture);

        Assert.NotNull(page);
        Assert.True(page.ReplacedOnEveryStart);
    }

    // "keep" only ever adds what is missing. A page that came from such a file is the editor's
    // to change, and a warning on it would be false.
    [Fact]
    public void DoesNotFindAPage_FromAFileThatKeepsWhatIsThere()
    {
        ShippedContent();

        Assert.Null(ImportedContentIndex.Load(_folder, "Production").Find(Sample));
    }

    // A file that is not imported into this environment cannot overwrite anything in it.
    [Fact]
    public void DoesNotFindAPage_FromAFileForAnotherEnvironment()
    {
        ShippedContent();

        Assert.Null(ImportedContentIndex.Load(_folder, "Production").Find(Fixture));
    }

    [Fact]
    public void DoesNotFindAPage_NoFileContains()
    {
        ShippedContent();

        Assert.Null(ImportedContentIndex.Load(_folder, "Production").Find(Guid.NewGuid()));
    }

    // The editor must open whatever state the import folder is in.
    [Theory]
    [InlineData(null)]
    [InlineData("{ not json")]
    [InlineData("""{ "imports": [ { "file": "missing.json", "environments": ["Production"], "existing": "replace" } ] }""")]
    [InlineData("""{ "imports": [ { "file": "../guide.json", "environments": ["Production"], "existing": "replace" } ] }""")]
    public void IsEmpty_RatherThanFailing_WhenTheFolderCannotBeRead(string? manifest)
    {
        Bundle("guide.json", Guide);
        if (manifest is not null) Manifest(manifest);

        var index = ImportedContentIndex.Load(_folder, "Production");

        Assert.Null(index.Find(Guide));
    }

    [Fact]
    public void TheShippedGuide_IsFoundInEveryEnvironment()
    {
        var folder = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!,
            "..", "..", "..", "src", "DfE.CheckPerformanceData.Web", "Data", "Import"));
        var guideRoot = new Guid("a969611f-33ad-518d-9ce5-dcd82a9b2656");

        foreach (var environment in new[] { "Development", "Review", "QA", "Preproduction", "Production" })
        {
            Assert.NotNull(ImportedContentIndex.Load(folder, environment).Find(guideRoot));
        }
    }

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
