using System.Text.Json;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentStaging;

// The web project's Data/Import folder holds the content the service ships with: a manifest and
// the content-staging bundles it lists, imported at start-up in every environment by
// ManifestContentImporter. These files are data, and nothing in the compiler checks them. The
// tests here pin what the importer and the people reading the pages rely on, so a file left out
// of the manifest, a broken link or a missing screenshot fails here and not in production.
//
// Most of them are about one file, cms-guide.json: the in-app guide "How to use the CMS",
// generated from the Markdown under docs/user-guides/cms.
public partial class ShippedContentImportTests
{
    private const string ImagePath = "/assets/cms-help/";

    private static readonly Guid GuideRootId = new("a969611f-33ad-518d-9ce5-dcd82a9b2656");

    private static readonly string[] ExistingPolicies = ["keep", "replace", "replaceOlder"];

    private static readonly string[] Environments = ["Development", "Review", "QA", "Preproduction", "Production"];

    private static string ImportFolder() =>
        Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "Data", "Import");

    private static ContentImportManifest Manifest() =>
        JsonSerializer.Deserialize<ContentImportManifest>(
            File.ReadAllText(Path.Combine(ImportFolder(), ManifestContentImporter.ManifestFileName)),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private static ContentBundle Load(string file) =>
        ContentStagingJson.Deserialize(File.ReadAllText(Path.Combine(ImportFolder(), file)))!;

    private static ContentBundle Guide() => Load("cms-guide.json");

    // ---- the manifest ----------------------------------------------------------------------

    [Fact]
    public void Manifest_ListsAtLeastOneFile()
    {
        Assert.NotEmpty(Manifest().Imports);
    }

    // A missing file is logged and skipped at start-up, so nothing else would notice.
    [Fact]
    public void EveryFileInTheManifest_ExistsAndIsABundleTheImporterAccepts()
    {
        foreach (var entry in Manifest().Imports)
        {
            Assert.True(File.Exists(Path.Combine(ImportFolder(), entry.File)),
                $"the manifest lists '{entry.File}', which is not in Data/Import");
            var bundle = Load(entry.File);
            Assert.Equal(ContentBundle.CurrentSchemaVersion, bundle.SchemaVersion);
            Assert.Empty(ContentBundleValidator.Validate(bundle).Where(i => i.Severity == ValidationSeverity.Fatal));
        }
    }

    // A file nobody listed is never imported, which is easy to do and hard to spot.
    [Fact]
    public void EveryFileInTheFolder_IsListedInTheManifest()
    {
        var listed = Manifest().Imports.Select(e => e.File).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unlisted = Directory.GetFiles(ImportFolder(), "*.json")
            .Select(Path.GetFileName)
            .Where(f => f != ManifestContentImporter.ManifestFileName && !listed.Contains(f!))
            .ToList();

        Assert.Empty(unlisted);
    }

    // The names are matched against ASPNETCORE_ENVIRONMENT. A misspelt one is not an error at
    // start-up, just a file that quietly never arrives in that environment.
    [Fact]
    public void EveryEntry_NamesEnvironmentsThatExist()
    {
        foreach (var entry in Manifest().Imports)
        {
            Assert.NotNull(entry.Environments);
            Assert.NotEmpty(entry.Environments);
            Assert.All(entry.Environments, name => Assert.Contains(name, Environments));
        }
    }

    [Fact]
    public void EveryEntry_SaysWhatToDoAboutExistingContent_InWordsTheImporterKnows()
    {
        foreach (var entry in Manifest().Imports)
        {
            Assert.Contains(entry.Existing ?? "keep", ExistingPolicies);
        }
    }

    // replaceOlder compares each page with the date the file was issued, so a file without one
    // could never update anything.
    [Fact]
    public void EveryReplaceOlderFile_SaysWhenItWasIssued()
    {
        foreach (var entry in Manifest().Imports.Where(e => e.Existing == "replaceOlder"))
        {
            var issued = Load(entry.File).ExportedAtUtc;
            Assert.NotNull(issued);
            Assert.True(issued < DateTime.UtcNow,
                $"'{entry.File}' is dated in the future, so every start-up would replace every page until that date passes");
        }
    }

    // Two files imported into the same tree: a shared id would make the later one silently
    // overwrite a page belonging to the earlier one.
    [Fact]
    public void NoTwoFiles_ShareAPage()
    {
        var ids = Manifest().Imports.SelectMany(e => Load(e.File).PageNodes.Select(p => p.Id)).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    // ---- the guide -------------------------------------------------------------------------

    // The guide is the service's own documentation: a release has to be able to update it, and
    // a correction made in one environment has to survive a restart. That is replaceOlder.
    [Fact]
    public void TheGuide_IsImportedSoThatAReleaseCanUpdateIt()
    {
        var entry = Manifest().Imports.Single(e => e.File == "cms-guide.json");

        Assert.Equal("replaceOlder", entry.Existing);
    }

    // The guide describes the service, so it belongs wherever the service runs.
    [Fact]
    public void TheGuide_IsImportedIntoEveryEnvironment()
    {
        var entry = Manifest().Imports.Single(e => e.File == "cms-guide.json");

        Assert.Equal(Environments.Order(), entry.Environments!.Order());
    }

    // The Help root is created at start-up under a pinned Guid. Parenting the guide to that Guid is
    // what lets a static file resolve against a fresh database.
    [Fact]
    public void TheGuideRoot_IsParentedToTheHelpRoot()
    {
        var bundle = Guide();
        var help = DefaultPageNodeRoots.All.Single(r => r.Segment == "help").Id;

        var root = bundle.PageNodes.Single(p => p.Id == GuideRootId);

        Assert.Equal(help, root.ParentId);
        Assert.Equal("how-to-use-the-cms", root.Segment);
    }

    // The importer never creates an orphan, so a page whose parent is not in the bundle would be
    // dropped with an error on every start-up.
    [Fact]
    public void EveryOtherPage_HangsOffAnotherPageInTheBundle()
    {
        var bundle = Guide();
        var ids = bundle.PageNodes.Select(p => p.Id).ToHashSet();

        foreach (var page in bundle.PageNodes.Where(p => p.Id != GuideRootId))
        {
            Assert.True(page.ParentId.HasValue && ids.Contains(page.ParentId.Value),
                $"guide page '{page.Segment}' is not parented to another guide page");
        }
    }

    // A draft-only page 404s for everyone who is not signed in as an editor.
    [Fact]
    public void EveryPage_HasAPublishedVersion()
    {
        var bundle = Guide();

        foreach (var page in bundle.PageNodes)
        {
            Assert.True(page.Versions.Any(v => v.PublishFrom is not null),
                $"guide page '{page.Segment}' has no published version, so it would 404 after seeding");
        }
    }

    // Pinned ids are how a later guide finds the pages an earlier one created.
    [Fact]
    public void EveryPage_HasAStableNonEmptyId()
    {
        var bundle = Guide();

        Assert.All(bundle.PageNodes, p => Assert.NotEqual(Guid.Empty, p.Id));
        Assert.Equal(bundle.PageNodes.Count, bundle.PageNodes.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void Bundle_HasNoDuplicateSegmentsUnderTheSameParent()
    {
        var bundle = Guide();

        var duplicates = bundle.PageNodes
            .GroupBy(p => (p.ParentId, p.Segment.ToLowerInvariant()))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.Item2)
            .ToList();

        Assert.Empty(duplicates);
    }

    // Separate bundles imported into the same tree: a shared id would make one silently overwrite
    // a page belonging to another.
    [Fact]
    public void GuideIds_DoNotCollideWithTheOtherSeedBundles()
    {
        var guide = Guide().PageNodes.Select(p => p.Id).ToHashSet();
        var samples = SampleContentSeedBundle.Load().PageNodes.Select(p => p.Id);
        var fixtures = TestFixtureSeedBundle.Load().PageNodes.Select(p => p.Id);

        Assert.Empty(guide.Intersect(samples.Concat(fixtures)));
    }

    // A guide nobody can find is not much of a guide. Every page stays in the search corpus and
    // carries the plain text the search index is built from.
    [Fact]
    public void EveryPage_CanBeFoundBySearch()
    {
        var bundle = Guide();

        foreach (var page in bundle.PageNodes)
        {
            Assert.True(page.AppearInSearch, $"guide page '{page.Segment}' is excluded from search");
            Assert.True(page.Versions.All(v => !string.IsNullOrWhiteSpace(v.BodyPlainText)),
                $"guide page '{page.Segment}' has no plain text for the search index");
        }
    }

    // Screenshots are served as static files from the web project. An image inlined as a data URI
    // puts megabytes into the page, the database row and this assembly, and cannot be cached.
    [Fact]
    public void NoPage_InlinesAnImage()
    {
        var bundle = Guide();

        foreach (var page in bundle.PageNodes)
        {
            Assert.DoesNotContain(page.Versions, v => v.Content.Contains("data:image", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void EveryScreenshot_ExistsInTheWebProject()
    {
        var images = Contents()
            .SelectMany(c => ImageReference().Matches(c).Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();

        Assert.NotEmpty(images);
        foreach (var image in images)
        {
            var file = Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "wwwroot",
                "assets", "cms-help", image);
            Assert.True(File.Exists(file), $"the guide shows '{ImagePath}{image}', which is not in the web project");
        }
    }

    // A screenshot nothing shows is dead weight in every deployment.
    [Fact]
    public void EveryScreenshotInTheWebProject_IsShownSomewhere()
    {
        var shown = Contents()
            .SelectMany(c => ImageReference().Matches(c).Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var folder = Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "wwwroot", "assets", "cms-help");

        var unused = Directory.GetFiles(folder).Select(Path.GetFileName).Where(f => !shown.Contains(f!)).ToList();

        Assert.Empty(unused);
    }

    // The accessible name of a screenshot is its alt text. Without one a screen reader announces
    // the file name.
    [Fact]
    public void EveryScreenshot_HasAltText()
    {
        foreach (var content in Contents())
        {
            foreach (Match img in ImageTag().Matches(content))
            {
                Assert.Matches(@"alt=\\?[""'][^""'\\]{10,}", img.Value);
            }
        }
    }

    [Fact]
    public void EveryLinkWithinTheGuide_PointsAtAPageInTheBundle()
    {
        var bundle = Guide();
        var byId = bundle.PageNodes.ToDictionary(p => p.Id);
        var paths = bundle.PageNodes.Select(p => PathOf(p, byId)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var links = Contents()
            .SelectMany(c => GuideLink().Matches(c).Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();

        Assert.NotEmpty(links);
        foreach (var link in links)
        {
            Assert.True(paths.Contains(link.Split('#')[0].TrimEnd('/')),
                $"the guide links to '{link}', which is not a page in the bundle");
        }
    }

    private static List<string> Contents() =>
        Guide().PageNodes.SelectMany(p => p.Versions).Select(v => v.Content).ToList();

    private static string PathOf(PageNodeBundleItem page, Dictionary<Guid, PageNodeBundleItem> byId)
    {
        var segments = new List<string>();
        for (var p = page; p is not null; p = p.ParentId is { } id && byId.TryGetValue(id, out var parent) ? parent : null)
            segments.Insert(0, p.Segment);
        return "/help/" + string.Join('/', segments);
    }

    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    [GeneratedRegex(@"/assets/cms-help/([A-Za-z0-9._-]+)")]
    private static partial Regex ImageReference();

    [GeneratedRegex(@"<img\b[^>]*>")]
    private static partial Regex ImageTag();

    [GeneratedRegex(@"href=\\?[""'](/help/how-to-use-the-cms[^""'\\]*)")]
    private static partial Regex GuideLink();
}
