using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.PageTree;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentStaging;

// The importer runs at start-up in every environment, production included: it reads a manifest
// from a folder and imports each content bundle the manifest lists, in the order listed. What is
// worth pinning is what it is allowed to touch. It hands each bundle to the ordinary
// content-staging importer; the per-page decisions it works out first are the whole of its policy.
public sealed class ManifestContentImporterTests : IDisposable
{
    private static readonly DateTime Issued = new(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Parent = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Child = Guid.Parse("11111111-0000-0000-0000-000000000002");
    private static readonly Guid Other = Guid.Parse("11111111-0000-0000-0000-000000000003");

    private readonly string _folder = Directory.CreateTempSubdirectory("cpd-import-").FullName;
    private readonly IPageNodeRepository _repository = Substitute.For<IPageNodeRepository>();
    private readonly IContentStagingService _staging = Substitute.For<IContentStagingService>();
    private readonly List<(ContentBundle Bundle, ContentImportMode Existing, IReadOnlyDictionary<Guid, ContentImportMode> Decisions, ContentImportMode New)> _imports = [];

    public ManifestContentImporterTests()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>()).Returns((PageNodeDto?)null);
        _repository.GetDeletedAsync().Returns([]);
        _repository.GetVersionsAsync(Arg.Any<Guid>()).Returns([]);
        _staging.ImportAsync(
                Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>(),
                Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>?>(), Arg.Any<ContentImportMode>())
            .Returns(call =>
            {
                _imports.Add((call.ArgAt<ContentBundle>(0), call.ArgAt<ContentImportMode>(1),
                    call.ArgAt<IReadOnlyDictionary<Guid, ContentImportMode>?>(2) ?? new Dictionary<Guid, ContentImportMode>(),
                    call.ArgAt<ContentImportMode>(3)));
                return new ContentImportResult { PageNodesCreated = 1 };
            });
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private ManifestContentImporter Importer() =>
        new(_repository, _staging, NullLogger<ManifestContentImporter>.Instance);

    private void Manifest(string json) => File.WriteAllText(Path.Combine(_folder, "manifest.json"), json);

    private void Bundle(string file, string exportedBy = "test", params (Guid Id, Guid? ParentId, string Segment)[] pages)
    {
        if (pages.Length == 0) pages = [(Parent, null, "parent"), (Child, Parent, "child")];
        var bundle = new ContentBundle
        {
            ExportedAtUtc = Issued,
            ExportedBy = exportedBy,
            PageNodes = pages.Select(p => new PageNodeBundleItem
            {
                Id = p.Id, ParentId = p.ParentId, Segment = p.Segment, Title = p.Segment, PageType = "content",
                Versions = [new PageNodeVersionBundleItem { VersionId = 1, PublishFrom = Issued, Content = "[]" }],
            }).ToList(),
        };
        File.WriteAllText(Path.Combine(_folder, file), ContentStagingJson.Serialize(bundle));
    }

    private void PageExists(Guid id, DateTime lastChanged)
    {
        _repository.GetByIdAsync(id).Returns(new PageNodeDto
        {
            Id = id, Segment = "s", Path = "s", Title = "t", PageType = "content",
        });
        _repository.GetVersionsAsync(id).Returns(
        [
            new PageNodeVersionDto { Content = "[]", UpdatedDate = lastChanged.AddDays(-30) },
            new PageNodeVersionDto { Content = "[]", UpdatedDate = lastChanged },
        ]);
    }

    private void PageWasDeleted(Guid id) =>
        _repository.GetDeletedAsync().Returns(
        [
            new PageNodeDto { Id = id, Segment = "s", Path = "s", Title = "t", PageType = "content", DeletedDate = Issued },
        ]);

    // ---- the manifest ----------------------------------------------------------------------

    [Fact]
    public async Task ImportsEachFile_InTheOrderTheManifestListsThem()
    {
        Bundle("b.json", exportedBy: "second");
        Bundle("a.json", exportedBy: "first");
        Manifest("""{ "imports": [ { "file": "a.json", "environments": ["Production"] }, { "file": "b.json", "environments": ["Production"] } ] }""");

        var summary = await Importer().RunAsync(_folder, "Production");

        Assert.Equal(["first", "second"], _imports.Select(i => i.Bundle.ExportedBy));
        Assert.Equal(2, summary.FilesImported);
    }

    // A folder with no manifest is an environment with nothing to import, not a fault.
    [Fact]
    public async Task DoesNothing_WhenThereIsNoManifest()
    {
        var summary = await Importer().RunAsync(_folder, "Production");

        Assert.Equal(0, summary.FilesImported);
        Assert.Empty(_imports);
    }

    [Fact]
    public async Task DoesNothing_WhenTheFolderDoesNotExist()
    {
        var summary = await Importer().RunAsync(Path.Combine(_folder, "missing"), "Production");

        Assert.Equal(0, summary.FilesImported);
    }

    // "Tries to import each": one bad entry must not stop the ones after it, and none of it may
    // stop the application starting.
    [Theory]
    [InlineData("""{ "file": "not-there.json", "environments": ["Production"] }""")]
    [InlineData("""{ "file": "broken.json", "environments": ["Production"] }""")]
    [InlineData("""{ "file": "../outside.json", "environments": ["Production"] }""")]
    [InlineData("""{ "file": "", "environments": ["Production"] }""")]
    [InlineData("""{ "file": "good.json" }""")]
    [InlineData("""{ "file": "good.json", "environments": [] }""")]
    public async Task SkipsAnEntryItCannotImport_AndCarriesOn(string badEntry)
    {
        File.WriteAllText(Path.Combine(_folder, "broken.json"), "{ this is not json");
        Bundle("good.json", exportedBy: "good");
        Manifest($$"""{ "imports": [ {{badEntry}}, { "file": "good.json", "environments": ["Production"] } ] }""");

        var summary = await Importer().RunAsync(_folder, "Production");

        Assert.Equal(["good"], _imports.Select(i => i.Bundle.ExportedBy));
        Assert.Equal(1, summary.FilesImported);
        Assert.Equal(1, summary.FilesFailed);
    }

    [Fact]
    public async Task CarriesOn_WhenAnImportThrows()
    {
        Bundle("a.json", exportedBy: "first");
        Bundle("b.json", exportedBy: "second");
        Manifest("""{ "imports": [ { "file": "a.json", "environments": ["Production"] }, { "file": "b.json", "environments": ["Production"] } ] }""");
        _staging.ImportAsync(
                Arg.Is<ContentBundle>(b => b.ExportedBy == "first"), Arg.Any<ContentImportMode>(),
                Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>?>(), Arg.Any<ContentImportMode>())
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        var summary = await Importer().RunAsync(_folder, "Production");

        Assert.Equal(1, summary.FilesFailed);
        Assert.Equal(1, summary.FilesImported);
    }

    [Fact]
    public async Task NeverThrows_WhenTheManifestItselfIsBroken()
    {
        Manifest("{ not json");

        var summary = await Importer().RunAsync(_folder, "Production");

        Assert.Equal(0, summary.FilesImported);
        Assert.Equal(1, summary.FilesFailed);
    }

    // A file name only. The manifest decides what is imported into production, so it must not
    // be able to reach outside its own folder.
    [Theory]
    [InlineData("sub/a.json")]
    [InlineData("..\\\\a.json")]
    [InlineData("/etc/a.json")]
    public async Task RefusesAFile_OutsideTheImportFolder(string file)
    {
        Bundle("a.json");
        Manifest($$"""{ "imports": [ { "file": "{{file}}", "environments": ["Production"] } ] }""");

        var summary = await Importer().RunAsync(_folder, "Production");

        Assert.Empty(_imports);
        Assert.Equal(1, summary.FilesFailed);
    }

    // Each entry names the environments it is for. Content that exists for the automated tests
    // has no business in production, and a new environment gets nothing until somebody decides
    // it should.
    [Theory]
    [InlineData("Development", 1)]
    [InlineData("Review", 1)]
    [InlineData("review", 1)]
    [InlineData("QA", 0)]
    [InlineData("Production", 0)]
    public async Task ImportsAFile_OnlyIntoTheEnvironmentsItsEntryNames(string environment, int expected)
    {
        Bundle("fixtures.json");
        Manifest("""{ "imports": [ { "file": "fixtures.json", "environments": ["Development", "Review"] } ] }""");

        var summary = await Importer().RunAsync(_folder, environment);

        Assert.Equal(expected, _imports.Count);
        Assert.Equal(0, summary.FilesFailed);
    }

    // ---- what an import may touch ----------------------------------------------------------

    // The default is the safe one: add what is missing and leave everything else exactly as it
    // is. Pressing restart on an environment full of an editor's work changes none of it.
    [Fact]
    public async Task ByDefault_AddsWhatIsMissing_AndKeepsWhatIsThere()
    {
        Bundle("a.json");
        Manifest("""{ "imports": [ { "file": "a.json", "environments": ["Production"] } ] }""");
        PageExists(Parent, lastChanged: Issued.AddYears(-1));

        await Importer().RunAsync(_folder, "Production");

        var import = Assert.Single(_imports);
        Assert.Equal(ContentImportMode.Skip, import.Existing);
        Assert.Equal(ContentImportMode.Replace, import.New);
        Assert.Empty(import.Decisions);
    }

    // For content nobody edits, such as test fixtures: put it back exactly as shipped, every time.
    [Fact]
    public async Task Replace_OverwritesWhatIsThere()
    {
        Bundle("a.json");
        Manifest("""{ "imports": [ { "file": "a.json", "environments": ["Production"], "existing": "replace" } ] }""");

        await Importer().RunAsync(_folder, "Production");

        Assert.Equal(ContentImportMode.Replace, Assert.Single(_imports).Existing);
    }

    // For content the service ships and updates, such as its own help pages. A page last changed
    // before the file was issued is replaced, which is how a release delivers a newer page.
    [Fact]
    public async Task ReplaceOlder_ReplacesAPage_LastChangedBeforeTheFileWasIssued()
    {
        Bundle("a.json");
        Manifest("""{ "imports": [ { "file": "a.json", "environments": ["Production"], "existing": "replaceOlder" } ] }""");
        PageExists(Parent, lastChanged: Issued.AddDays(-1));

        await Importer().RunAsync(_folder, "Production");

        var import = Assert.Single(_imports);
        Assert.Equal(ContentImportMode.Skip, import.Existing);
        Assert.Equal(ContentImportMode.Replace, import.Decisions[Parent]);
    }

    // Two things rely on this. Replacing a page stamps it with the time of the import, which is
    // later than the issue date, so the next start-up leaves it alone instead of rewriting every
    // page on every restart. And a correction made in one environment survives until a newer
    // file is released.
    [Fact]
    public async Task ReplaceOlder_LeavesAPageAlone_WhenItHasChangedSinceTheFileWasIssued()
    {
        Bundle("a.json");
        Manifest("""{ "imports": [ { "file": "a.json", "environments": ["Production"], "existing": "replaceOlder" } ] }""");
        PageExists(Parent, lastChanged: Issued.AddMinutes(5));

        await Importer().RunAsync(_folder, "Production");

        Assert.False(Assert.Single(_imports).Decisions.ContainsKey(Parent));
    }

    // A page whose versions were all removed renders nothing and has no date to compare. It is
    // treated as older than any file, which is what repairs it.
    [Fact]
    public async Task ReplaceOlder_ReplacesAPage_ThatHasNoVersionsLeft()
    {
        Bundle("a.json");
        Manifest("""{ "imports": [ { "file": "a.json", "environments": ["Production"], "existing": "replaceOlder" } ] }""");
        PageExists(Parent, lastChanged: Issued.AddDays(1));
        _repository.GetVersionsAsync(Parent).Returns([]);

        await Importer().RunAsync(_folder, "Production");

        Assert.Equal(ContentImportMode.Replace, Assert.Single(_imports).Decisions[Parent]);
    }

    // Without a date there is nothing to compare, and guessing would mean either never updating
    // or overwriting on every start-up.
    [Fact]
    public async Task ReplaceOlder_FailsTheEntry_WhenTheFileDoesNotSayWhenItWasIssued()
    {
        File.WriteAllText(Path.Combine(_folder, "a.json"),
            ContentStagingJson.Serialize(new ContentBundle { PageNodes = [] }));
        Manifest("""{ "imports": [ { "file": "a.json", "environments": ["Production"], "existing": "replaceOlder" } ] }""");

        var summary = await Importer().RunAsync(_folder, "Production");

        Assert.Empty(_imports);
        Assert.Equal(1, summary.FilesFailed);
    }

    [Fact]
    public async Task FailsTheEntry_WhenExistingIsNotAWordItKnows()
    {
        Bundle("a.json");
        Manifest("""{ "imports": [ { "file": "a.json", "environments": ["Production"], "existing": "obliterate" } ] }""");

        var summary = await Importer().RunAsync(_folder, "Production");

        Assert.Empty(_imports);
        Assert.Equal(1, summary.FilesFailed);
    }

    // Deleting is a decision, and a restart must not quietly undo it — whatever the entry says
    // about existing content. It also has to be said explicitly: a deleted page is invisible to
    // the importer's own lookup, so left to the new-item default it would be created a second
    // time under an identity that is already taken, and fail on every start-up.
    [Theory]
    [InlineData("keep")]
    [InlineData("replace")]
    [InlineData("replaceOlder")]
    public async Task KeepsADeletedPageDeleted_AlongWithEverythingTheFilePutsBeneathIt(string existing)
    {
        Bundle("a.json", "test", (Parent, null, "parent"), (Child, Parent, "child"), (Other, null, "other"));
        Manifest($$"""{ "imports": [ { "file": "a.json", "environments": ["Production"], "existing": "{{existing}}" } ] }""");
        PageWasDeleted(Parent);

        await Importer().RunAsync(_folder, "Production");

        var decisions = Assert.Single(_imports).Decisions;
        Assert.Equal(ContentImportMode.Skip, decisions[Parent]);
        Assert.Equal(ContentImportMode.Skip, decisions[Child]);
        Assert.False(decisions.ContainsKey(Other));
    }

    [Fact]
    public async Task ReportsThePagesCreatedAndReplaced()
    {
        Bundle("a.json");
        Manifest("""{ "imports": [ { "file": "a.json", "environments": ["Production"] } ] }""");
        _staging.ImportAsync(
                Arg.Any<ContentBundle>(), Arg.Any<ContentImportMode>(),
                Arg.Any<IReadOnlyDictionary<Guid, ContentImportMode>?>(), Arg.Any<ContentImportMode>())
            .Returns(new ContentImportResult { PageNodesCreated = 3, PageNodesUpdated = 2, PageNodesSkipped = 9 });

        var summary = await Importer().RunAsync(_folder, "Production");

        Assert.Equal(5, summary.PagesChanged);
    }
}
