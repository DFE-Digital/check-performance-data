using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Application.UnitTests.ContentStaging;

namespace DfE.CheckPerformanceData.Application.UnitTests.PageTree;

// The fixture bundle is the content the browser-test suite navigates to. It is a content-staging
// bundle in exactly the same format as the sample content, and for the same reason: the pages are
// what the CMS produced rather than a second, hand-written notion of what a page looks like.
//
// What separates the two bundles is ownership. Sample content is demonstration material an editor
// may reasonably edit, rename or delete; fixture content belongs to the test suite, lives under
// its own root so it never sits alongside an editor's work, and is re-imported over the top on
// every start so an emptied page comes back. The bundle is a file in the web project's Data/Import
// folder, imported with the rest of the content the service ships with. These tests pin the
// properties the suite relies on — a bundle is data, and nothing in the compiler checks it.
public class TestFixtureSeedBundleTests
{
    [Fact]
    public void Bundle_IsInTheImportFolderAndParses()
    {
        var bundle = ShippedContent.Fixtures();

        Assert.NotNull(bundle);
        Assert.NotEmpty(bundle.PageNodes);
    }

    [Fact]
    public void Bundle_DeclaresTheSchemaVersionTheImporterAccepts()
    {
        var bundle = ShippedContent.Fixtures();

        Assert.Equal(ContentBundle.CurrentSchemaVersion, bundle.SchemaVersion);
    }

    // Everything in this bundle hangs off the fixture root, which is the whole point of it: an
    // editor browsing the page tree sees one clearly-named container rather than test scaffolding
    // interleaved with their own pages. A fixture parented anywhere else would defeat that and —
    // because the importer resolves parents by Guid — would be dropped as an orphan on a fresh
    // database anyway.
    [Fact]
    public void EveryFixturePage_IsBeneathTheDevelopmentTestingRoot()
    {
        var bundle = ShippedContent.Fixtures();
        var byId = bundle.PageNodes.ToDictionary(p => p.Id);

        foreach (var page in bundle.PageNodes.Where(p => p.Id != DefaultPageNodeRoots.DevelopmentTestingRootId))
        {
            var top = page;
            while (top.ParentId is { } parent && byId.TryGetValue(parent, out var above)) top = above;

            Assert.Equal(DefaultPageNodeRoots.DevelopmentTestingRootId, top.Id);
        }
    }

    // The importer creates pages in file order and never creates an orphan, so a page listed
    // ahead of the folder it sits in would be dropped on a fresh database.
    [Fact]
    public void EveryFixturePage_ComesAfterItsParentInTheFile()
    {
        var seen = new HashSet<Guid>();

        foreach (var page in ShippedContent.Fixtures().PageNodes)
        {
            Assert.True(page.ParentId is null || seen.Contains(page.ParentId.Value),
                $"'{page.Segment}' is listed before its parent");
            seen.Add(page.Id);
        }
    }

    // The widget test pages have a folder of their own, so the pages individual tests rely on
    // are not lost among them. Like the root, it is a folder kept out of the menus.
    [Fact]
    public void TheWidgetTestPages_HaveAFolderOfTheirOwn_UnderTheRoot()
    {
        var folder = ShippedContent.Fixtures().PageNodes.Single(p => p.Id == TestFixtureSeedBundle.WidgetsFolderId);

        Assert.Equal(DefaultPageNodeRoots.DevelopmentTestingRootId, folder.ParentId);
        Assert.Equal("widgets", folder.Segment);
        Assert.Equal("Widgets", folder.Title);
        Assert.Equal("folder", folder.PageType);
        Assert.False(folder.ShowInMenu);
    }

    // The root travels in the file, so the file is everything an environment needs: nothing else
    // creates /development-testing. It comes first because the importer creates pages in file
    // order and never creates an orphan, and it carries the pinned id the fixtures name as their
    // parent.
    [Fact]
    public void TheBundle_BringsItsOwnRoot_AheadOfThePagesBeneathIt()
    {
        var root = ShippedContent.Fixtures().PageNodes[0];

        Assert.Equal(DefaultPageNodeRoots.DevelopmentTestingRootId, root.Id);
        Assert.Null(root.ParentId);
        Assert.Equal(DefaultPageNodeRoots.DevelopmentTestingSegment, root.Segment);
    }

    // Folder is what keeps the root out of search results — the search query filters folders
    // structurally — and hiding it from the menus keeps it out of the site navigation. Between
    // them there is no route by which an ordinary visitor arrives at the fixture tree.
    [Fact]
    public void TheRoot_IsAFolderHiddenFromTheMenusAndFromSearch()
    {
        var root = ShippedContent.Fixtures().PageNodes[0];

        Assert.Equal("folder", root.PageType);
        Assert.False(root.ShowInMenu);
        Assert.False(root.AppearInSearch);
        Assert.Empty(root.Versions);
    }

    // A draft-only fixture 404s, which is precisely the failure the suite was hitting before the
    // fixtures existed.
    [Fact]
    public void EveryFixturePage_HasAPublishedVersion()
    {
        var bundle = ShippedContent.Fixtures();

        foreach (var page in bundle.PageNodes.Where(p => p.PageType != "folder"))
        {
            Assert.True(page.Versions.Any(v => v.PublishFrom is not null),
                $"fixture '{page.Segment}' has no published version, so it would 404 after seeding");
        }
    }

    // The wiki render path takes a different code path from content pages (raw HTML through
    // IHtmlRenderingService rather than a widget tree). The browser tests that cover it navigate
    // to a fixture, so the bundle has to keep one wiki-typed page for that coverage to exist.
    [Fact]
    public void Bundle_KeepsAWikiTypedFixture_SoTheWikiRenderPathStaysCovered()
    {
        var bundle = ShippedContent.Fixtures();

        Assert.Contains(bundle.PageNodes, p => p.PageType == "wiki");
    }

    // The back-to-top contract has two halves — a page long enough to scroll, and a page that
    // fits the viewport and must therefore never reveal the link. Both need a fixture.
    [Theory]
    [InlineData(TestFixtureSeedBundle.LongPageSegment)]
    [InlineData(TestFixtureSeedBundle.ShortPageSegment)]
    [InlineData(TestFixtureSeedBundle.SearchFixtureSegment)]
    public void Bundle_CarriesTheFixturesTheSuiteNavigatesTo(string segment)
    {
        var bundle = ShippedContent.Fixtures();

        Assert.Contains(bundle.PageNodes, p => p.Segment == segment);
    }

    // The long fixture has to out-scroll the viewport or every assertion the back-to-top suite
    // makes about reveal timing is vacuous. Body length is a coarse proxy, but it is the one
    // thing that can be checked without a browser, and it catches the "someone trimmed the
    // filler paragraphs" edit that would otherwise only surface as a red browser test.
    [Fact]
    public void TheLongFixture_IsSubstantiallyLongerThanTheShortOne()
    {
        var bundle = ShippedContent.Fixtures();

        var longBody = BodyLength(bundle, TestFixtureSeedBundle.LongPageSegment);
        var shortBody = BodyLength(bundle, TestFixtureSeedBundle.ShortPageSegment);

        Assert.True(longBody > 1000,
            $"the long fixture body is {longBody} characters — too short to out-scroll a laptop viewport");
        Assert.True(shortBody < 300,
            $"the short fixture body is {shortBody} characters — long enough to scroll, which makes the " +
            "'never reveals on a short page' assertion meaningless");
    }

    // Two of the admin analytics tests drive a real site search and then assert the prior-search
    // panel lists hits. They used to search for a word that merely happened to be somewhere in the
    // corpus; the fixture now owns a term of its own so the query is guaranteed to match. That only
    // works while the fixture carries the term AND is visible to search.
    [Fact]
    public void TheSearchFixture_CarriesTheSearchTerm_AndIsVisibleToSearch()
    {
        var bundle = ShippedContent.Fixtures();

        var page = bundle.PageNodes.Single(p => p.Segment == TestFixtureSeedBundle.SearchFixtureSegment);

        Assert.True(page.AppearInSearch, "the search fixture is excluded from search, so it can never be a hit");
        Assert.Contains(TestFixtureSeedBundle.SearchTerm, page.Keywords ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    // The other two fixtures exist to be navigated to directly, not to be found. Leaving them in
    // the search corpus would put test scaffolding in an editor's search results for no gain.
    [Theory]
    [InlineData(TestFixtureSeedBundle.LongPageSegment)]
    [InlineData(TestFixtureSeedBundle.ShortPageSegment)]
    public void TheBackToTopFixtures_AreKeptOutOfSearch(string segment)
    {
        var bundle = ShippedContent.Fixtures();

        var page = bundle.PageNodes.Single(p => p.Segment == segment);

        Assert.False(page.AppearInSearch);
    }

    [Fact]
    public void Bundle_HasNoDuplicateSegmentsUnderTheSameParent()
    {
        var bundle = ShippedContent.Fixtures();

        var duplicates = bundle.PageNodes
            .GroupBy(p => (p.ParentId, p.Segment.ToLowerInvariant()))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.Item2)
            .ToList();

        Assert.Empty(duplicates);
    }

    // Pinned ids, for the same reason the roots are pinned: the seed re-imports over the top of
    // whatever is already there, and it can only find the row to repair if the identity is stable
    // across environments.
    [Fact]
    public void EveryFixturePage_HasAStableNonEmptyId()
    {
        var bundle = ShippedContent.Fixtures();

        Assert.All(bundle.PageNodes, p => Assert.NotEqual(Guid.Empty, p.Id));
        Assert.Equal(bundle.PageNodes.Count, bundle.PageNodes.Select(p => p.Id).Distinct().Count());
    }

    // The fixture identities must not collide with the sample content's. They are separate
    // bundles imported one after the other, and a shared id would make the second import
    // silently overwrite a page belonging to the first.
    [Fact]
    public void FixtureIds_DoNotCollideWithTheSampleContent()
    {
        var fixtures = ShippedContent.Fixtures().PageNodes.Select(p => p.Id).ToHashSet();
        var samples = SampleContentSeedBundle.Load().PageNodes.Select(p => p.Id).ToHashSet();

        Assert.Empty(fixtures.Intersect(samples));
    }

    // ---- a test page for each widget ---------------------------------------------------------

    // Every widget an author can place has a page here that shows it set up in several ways, for
    // the browser tests to look at and for a developer to check a change against. The list comes
    // from the registry, so a new widget fails here until it has a page of its own.
    public static TheoryData<string, string> Widgets()
    {
        var data = new TheoryData<string, string>();
        foreach (var w in WidgetRegistry.All) data.Add(w.Type, w.PaletteLabel);
        return data;
    }

    private static PageNodeBundleItem TestPageFor(string label) =>
        ShippedContent.Fixtures().PageNodes.Single(p => p.Title == $"{label} test page");

    private static IReadOnlyList<ContentNode> TreeOf(PageNodeBundleItem page) =>
        ContentPageJson.Deserialize(page.Versions.Single().Content)!;

    [Theory]
    [MemberData(nameof(Widgets))]
    public void EveryWidget_HasATestPage_NamedAfterIt(string type, string label)
    {
        var page = TestPageFor(label);

        Assert.Equal("content", page.PageType);
        Assert.Equal(label.ToLowerInvariant().Replace(' ', '-') + "-test-page", page.Segment);
        Assert.Equal(TestFixtureSeedBundle.WidgetsFolderId, page.ParentId);
        Assert.Contains(ContentTreeWalker.AllWidgets(TreeOf(page)), w => w.Type == type);
    }

    // "Several ways" is the point of the page: one widget dropped on it once shows nothing a
    // sample page does not. A way is a different set of options or a different width of column.
    [Theory]
    [MemberData(nameof(Widgets))]
    public void EachTestPage_ShowsItsWidgetAtLeastThreeWays(string type, string label)
    {
        var ways = new HashSet<string>();
        foreach (var region in TreeOf(TestPageFor(label)).OfType<RegionNode>())
        {
            foreach (var widget in ContentTreeWalker.AllWidgets([region]).Where(w => w.Type == type))
                ways.Add($"{region.Layout}:{region.Columns.Count(c => c.Count > 0)}:{widget.Props?.ToJsonString()}");
        }

        Assert.True(ways.Count >= 3, $"the {label} test page shows the widget {ways.Count} way(s)");
    }

    // An imported page has not been through the editor, which is what gives a heading its anchor.
    // Without one the heading has no id, and a Page navigation widget links to nothing.
    [Theory]
    [MemberData(nameof(Widgets))]
    public void EveryHeadingOnATestPage_HasTheAnchorTheEditorWouldGiveIt(string type, string label)
    {
        _ = type;
        var tree = TreeOf(TestPageFor(label));
        var shipped = ContentTreeWalker.AllWidgets(tree).Where(w => w.Type == "heading").Select(w => w.Anchor).ToList();

        HeadingAnchorizer.Apply(tree);
        var allocated = ContentTreeWalker.AllWidgets(tree).Where(w => w.Type == "heading").Select(w => w.Anchor).ToList();

        Assert.NotEmpty(shipped);
        Assert.Equal(allocated, shipped);
    }

    [Theory]
    [MemberData(nameof(Widgets))]
    public void ATestPage_UsesOnlyWidgetsTheRegistryKnows(string type, string label)
    {
        _ = type;

        Assert.All(ContentTreeWalker.AllWidgets(TreeOf(TestPageFor(label))), w => Assert.True(WidgetRegistry.IsKnown(w.Type), w.Type));
    }

    // A search for the search fixture's own word has to match that page and nothing else, and
    // test scaffolding has no place in anybody's search results.
    [Theory]
    [MemberData(nameof(Widgets))]
    public void ATestPage_IsKeptOutOfSearch(string type, string label)
    {
        _ = type;

        Assert.False(TestPageFor(label).AppearInSearch);
    }

    // ---- a test page for the regions -------------------------------------------------------

    private static PageNodeBundleItem RegionsPage() =>
        ShippedContent.Fixtures().PageNodes.Single(p => p.Title == "Regions test page");

    private static IEnumerable<RegionNode> AllRegions(IEnumerable<ContentNode> nodes) =>
        nodes.OfType<RegionNode>().SelectMany(r => new[] { r }.Concat(r.Columns.SelectMany(c => AllRegions(c))));

    // Beside the folder of widget pages, not in it: a region is not a widget.
    [Fact]
    public void TheRegionsTestPage_SitsUnderTheRoot_BesideTheWidgetsFolder()
    {
        var page = RegionsPage();

        Assert.Equal(DefaultPageNodeRoots.DevelopmentTestingRootId, page.ParentId);
        Assert.Equal("regions-test-page", page.Segment);
        Assert.False(page.AppearInSearch);
    }

    // The list comes from the layouts themselves, so a new layout fails here until the page has
    // a region in it.
    [Fact]
    public void TheRegionsTestPage_HasARegionInEveryLayout()
    {
        var used = AllRegions(TreeOf(RegionsPage())).Select(r => r.Layout).ToHashSet();

        Assert.Equal(Enum.GetValues<RegionLayout>().Order(), used.Order());
    }

    // A region can go inside a column of another. The page has one, so that is seen too.
    [Fact]
    public void TheRegionsTestPage_HasARegionInsideAColumn()
    {
        var nested = TreeOf(RegionsPage()).OfType<RegionNode>()
            .SelectMany(r => r.Columns).SelectMany(c => c).OfType<RegionNode>();

        Assert.NotEmpty(nested);
    }

    private static int BodyLength(ContentBundle bundle, string segment) =>
        bundle.PageNodes.Single(p => p.Segment == segment)
            .Versions.Sum(v => (v.BodyPlainText ?? string.Empty).Length);

    // ---- the media library samples ---------------------------------------------------------

    // The fixture pages show library images, so the file carries those images too: they arrive
    // with the pages, wherever the pages are imported, before them. A page that names an image the
    // file does not carry shows nothing there, and the import warns about it.
    private static IEnumerable<Guid> ImagesUsedBy(ContentBundle bundle) =>
        bundle.PageNodes.SelectMany(p => p.Versions).SelectMany(v => MediaReferenceExtractor.Extract(v.Content))
            .Concat(bundle.ContentBlocks.Where(b => b.BlockType == "Content").SelectMany(b => MediaReferenceExtractor.ExtractRichText(b.Value)))
            .Select(r => r.AssetId);

    [Fact]
    public void EveryImageTheFixturesUse_IsInTheFile_WithItsBytes()
    {
        var bundle = ShippedContent.Fixtures();
        var files = bundle.MediaFiles.ToDictionary(f => f.Sha256);

        foreach (var id in ImagesUsedBy(bundle).Distinct())
        {
            var asset = Assert.Single(bundle.MediaAssets, a => a.Id == id);
            var current = Assert.Single(asset.Versions, v => v.Version == asset.CurrentVersion);
            Assert.True(files.TryGetValue(current.Sha256, out var file), $"no file for image {id}");
            Assert.Equal(current.Sha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(file.Data)));
            Assert.Equal(current.SizeBytes, file.Data.Length);
        }
    }

    // The Image test page was written against these two ids: a PNG about twice as wide as it is
    // tall, so the crops show, and an SVG.
    [Fact]
    public void TheImageTestPage_HasItsPngAndSvg_InTheFile()
    {
        var bundle = ShippedContent.Fixtures();

        var png = bundle.MediaAssets.Single(a => a.Id == new Guid("00000000-cd94-4a01-8f01-0000000e0201")).Versions.Single();
        Assert.Equal("image/png", png.ContentType);
        Assert.Equal(2.0, (double)png.Width!.Value / png.Height!.Value, 1);
        Assert.True(png.Width >= 1280, "the PNG is too narrow to have resized copies at each width");
        Assert.Equal("image/svg+xml", bundle.MediaAssets.Single(a => a.Id == new Guid("00000000-cd94-4a01-8f01-0000000e0202")).Versions.Single().ContentType);
    }

    [Fact]
    public void TheSamples_AreAPngAPhotoAnSvgAndAnAnimation()
    {
        var versions = ShippedContent.Fixtures().MediaAssets.Select(a => a.Versions.Single()).ToList();

        Assert.Contains(versions, v => v.ContentType == "image/png");
        Assert.Contains(versions, v => v.ContentType == "image/jpeg");
        Assert.Contains(versions, v => v.ContentType == "image/svg+xml");
        Assert.Contains(versions, v => v.ContentType == "image/gif" && v.IsAnimated);
    }

    // Move embedded images needs something to move on a new stack.
    [Fact]
    public void TheEmbeddedImageTestPage_HoldsADataImage()
    {
        var page = ShippedContent.Fixtures().PageNodes.Single(p => p.Segment == "embedded-image-test-page");

        Assert.Contains("src=\\\"data:image/png;base64,", page.Versions.Single().Content);
    }

    [Theory]
    [InlineData("media-library-test-page")]
    [InlineData("media-rich-text-test-page")]
    [InlineData("embedded-image-test-page")]
    public void TheMediaTestPages_AreUnderTheRoot_PublishedAndOutOfSearch(string segment)
    {
        var page = ShippedContent.Fixtures().PageNodes.Single(p => p.Segment == segment);

        Assert.Equal(DefaultPageNodeRoots.DevelopmentTestingRootId, page.ParentId);
        Assert.False(page.AppearInSearch);
        // Live already: a publish time still to come leaves the page a 404 until then.
        Assert.True(page.Versions.Single().PublishFrom <= DateTime.UtcNow, $"{segment} is not live yet");
        var tree = ContentPageJson.Deserialize(page.Versions.Single().Content)!;
        var shipped = ContentTreeWalker.AllWidgets(tree).Where(w => w.Type == "heading").Select(w => w.Anchor).ToList();
        HeadingAnchorizer.Apply(tree);
        Assert.Equal(ContentTreeWalker.AllWidgets(tree).Where(w => w.Type == "heading").Select(w => w.Anchor).ToList(), shipped);
    }

    // The guide has steps for deleting unused images, the picker's Media grant, the focal point
    // that a preset crop turns off, and an import where the bundle's details win.
    [Fact]
    public void TheMediaLibraryGuide_HasStepsForEachLibraryChange()
    {
        var content = ShippedContent.Fixtures().PageNodes.Single(p => p.Segment == "media-library-test-page").Versions.Single().Content;

        Assert.Contains("\"text\": \"Delete unused images\"", content);
        Assert.Contains("href=\\\"/admin/media?unused=true\\\"", content);
        Assert.Contains("\"text\": \"The picker needs the Media section\"", content);
        Assert.Contains("is now turned off, and a hint says why", content);
        Assert.Contains("The bundle wins.", content);
    }

    // A rich-text image marked decorative in the editor: an empty alt and role="presentation".
    [Fact]
    public void TheMediaRichTextTestPage_HasADecorativeImage()
    {
        var page = ShippedContent.Fixtures().PageNodes.Single(p => p.Segment == "media-rich-text-test-page");

        Assert.Contains("alt=\\\"\\\" width=\\\"1280\\\" height=\\\"640\\\" role=\\\"presentation\\\"", page.Versions.Single().Content);
    }

    // The guide has steps for the library's views and for an image the editor gives another shape.
    [Fact]
    public void TheMediaLibraryGuide_HasStepsForTheViews_AndForAnImageOfAnotherShape()
    {
        var content = ShippedContent.Fixtures().PageNodes.Single(p => p.Segment == "media-library-test-page").Versions.Single().Content;

        Assert.Contains("choose “Details” in the View list", content);
        Assert.Contains("Choose “Large”", content);
        Assert.Contains("Select “Constrain proportions” to unlock it", content);
    }

    // A wide sample image that the editor stretched into a square box. The page shows the square.
    [Fact]
    public void TheMediaRichTextTestPage_HasAnImageInAnotherShapeThanItsFile()
    {
        var page = ShippedContent.Fixtures().PageNodes.Single(p => p.Segment == "media-rich-text-test-page");

        Assert.Contains("blocks.png\\\" alt=\\\"Coloured blocks, stretched into a square\\\" width=\\\"320\\\" height=\\\"320\\\"", page.Versions.Single().Content);
    }

    // The guide has steps for folders: the nav tree, include subfolders, create, move and the picker.
    [Fact]
    public void TheMediaLibraryGuide_HasStepsForFolders()
    {
        var content = ShippedContent.Fixtures().PageNodes.Single(p => p.Segment == "media-library-test-page").Versions.Single().Content;

        Assert.Contains("\"text\": \"Folders in the media library\"", content);
        Assert.Contains("href=\\\"/admin/media?folder=00000000-cd94-4a01-8f01-0000000e0301\\\"", content);
        Assert.Contains("Untick “Find images in this folder and its subfolders”", content);
        Assert.Contains("“Move selected images”", content);
    }

    // The sample images sit in sample folders, so a fresh stack has folders to try. The animation
    // stays at the top of the library.
    [Fact]
    public void TheSampleImages_AreInSampleFolders()
    {
        var assets = ShippedContent.Fixtures().MediaAssets.ToDictionary(a => a.Id.ToString());

        Assert.Equal(["Samples", "Graphics"], assets["00000000-cd94-4a01-8f01-0000000e0201"].FolderPath!.Select(f => f.Name));
        Assert.Equal(["Samples", "Photos"], assets["00000000-cd94-4a01-8f01-0000000e0203"].FolderPath!.Select(f => f.Name));
        Assert.Empty(assets["00000000-cd94-4a01-8f01-0000000e0204"].FolderPath!);
    }
}
