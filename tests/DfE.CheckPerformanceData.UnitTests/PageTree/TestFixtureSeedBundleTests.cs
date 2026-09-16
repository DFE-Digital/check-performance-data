using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Application.UnitTests.PageTree;

// The fixture bundle is the content the browser-test suite navigates to. It is a content-staging
// bundle in exactly the same format as the sample content, and for the same reason: the pages are
// what the CMS produced rather than a second, hand-written notion of what a page looks like.
//
// What separates the two bundles is ownership. Sample content is demonstration material an editor
// may reasonably edit, rename or delete; fixture content belongs to the test suite, lives under
// its own root so it never sits alongside an editor's work, and is re-imported over the top on
// every seed so an emptied page comes back. These tests pin the properties the suite relies on —
// a bundle is data, and nothing in the compiler checks it.
public class TestFixtureSeedBundleTests
{
    [Fact]
    public void Bundle_IsEmbeddedAndParses()
    {
        var bundle = TestFixtureSeedBundle.Load();

        Assert.NotNull(bundle);
        Assert.NotEmpty(bundle.PageNodes);
    }

    [Fact]
    public void Bundle_DeclaresTheSchemaVersionTheImporterAccepts()
    {
        var bundle = TestFixtureSeedBundle.Load();

        Assert.Equal(ContentBundle.CurrentSchemaVersion, bundle.SchemaVersion);
    }

    // Everything in this bundle hangs off the fixture root, which is the whole point of it: an
    // editor browsing the page tree sees one clearly-named container rather than test scaffolding
    // interleaved with their own pages. A fixture parented anywhere else would defeat that and —
    // because the importer resolves parents by Guid — would be dropped as an orphan on a fresh
    // database anyway.
    [Fact]
    public void EveryFixturePage_IsParentedToTheDevelopmentTestingRoot()
    {
        var bundle = TestFixtureSeedBundle.Load();

        foreach (var page in bundle.PageNodes)
        {
            Assert.True(page.ParentId.HasValue,
                $"fixture '{page.Segment}' has no parent — the root comes from the seeder, not this bundle");
            Assert.Equal(DefaultPageNodeRoots.DevelopmentTestingRootId, page.ParentId!.Value);
        }
    }

    // A draft-only fixture 404s, which is precisely the failure the suite was hitting before the
    // fixtures existed.
    [Fact]
    public void EveryFixturePage_HasAPublishedVersion()
    {
        var bundle = TestFixtureSeedBundle.Load();

        foreach (var page in bundle.PageNodes)
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
        var bundle = TestFixtureSeedBundle.Load();

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
        var bundle = TestFixtureSeedBundle.Load();

        Assert.Contains(bundle.PageNodes, p => p.Segment == segment);
    }

    // The long fixture has to out-scroll the viewport or every assertion the back-to-top suite
    // makes about reveal timing is vacuous. Body length is a coarse proxy, but it is the one
    // thing that can be checked without a browser, and it catches the "someone trimmed the
    // filler paragraphs" edit that would otherwise only surface as a red browser test.
    [Fact]
    public void TheLongFixture_IsSubstantiallyLongerThanTheShortOne()
    {
        var bundle = TestFixtureSeedBundle.Load();

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
        var bundle = TestFixtureSeedBundle.Load();

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
        var bundle = TestFixtureSeedBundle.Load();

        var page = bundle.PageNodes.Single(p => p.Segment == segment);

        Assert.False(page.AppearInSearch);
    }

    [Fact]
    public void Bundle_HasNoDuplicateSegmentsUnderTheSameParent()
    {
        var bundle = TestFixtureSeedBundle.Load();

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
        var bundle = TestFixtureSeedBundle.Load();

        Assert.All(bundle.PageNodes, p => Assert.NotEqual(Guid.Empty, p.Id));
        Assert.Equal(bundle.PageNodes.Count, bundle.PageNodes.Select(p => p.Id).Distinct().Count());
    }

    // The fixture identities must not collide with the sample content's. They are separate
    // bundles imported one after the other, and a shared id would make the second import
    // silently overwrite a page belonging to the first.
    [Fact]
    public void FixtureIds_DoNotCollideWithTheSampleContent()
    {
        var fixtures = TestFixtureSeedBundle.Load().PageNodes.Select(p => p.Id).ToHashSet();
        var samples = SampleContentSeedBundle.Load().PageNodes.Select(p => p.Id).ToHashSet();

        Assert.Empty(fixtures.Intersect(samples));
    }

    private static int BodyLength(ContentBundle bundle, string segment) =>
        bundle.PageNodes.Single(p => p.Segment == segment)
            .Versions.Sum(v => (v.BodyPlainText ?? string.Empty).Length);
}
