namespace DfE.CheckPerformanceData.Application.PageTree;

// The content the automated browser tests navigate to: a content-staging bundle the CMS exported,
// rather than pages assembled in C# that can drift from what an editor's output looks like.
//
// The bundle is a file in the web project's Data/Import folder, listed in the manifest there and
// imported by ManifestContentImporter like the rest of the content the service ships with. It
// holds the /development-testing root as well as the pages beneath it, so the file is everything
// an environment needs. The root is a folder, which keeps it out of search results, and is hidden
// from the menus, so no ordinary visitor arrives at the fixture tree; an editor browsing the page
// tree sees one plainly-labelled container rather than test pages interleaved with their own.
//
// Fixtures belong to the test suite. The manifest imports them with "existing": "replace", so they
// are put back as shipped on every start: that is the only thing that brings back a page whose
// versions were deleted. A page emptied that way still exists, so an import that keeps what is
// there would walk straight past it and the route would 404 for good.
//
// Alongside the pages individual tests need, there is a test page for each widget an author can
// place ("Heading test page", "Search test page" and so on), showing the widget set up in several
// ways. They are together in a folder, /development-testing/widgets. A new widget needs one too.
//
// To change a fixture: edit the page through the CMS, export /development-testing from
// /admin/content-staging, and replace the file.
public static class TestFixtureSeedBundle
{
    /// <summary>The bundle's file name in the import folder, as the manifest lists it.</summary>
    public const string FileName = "development-testing.json";

    /// <summary>The folder under the root that holds a test page for each widget.</summary>
    public static readonly Guid WidgetsFolderId = new("00000000-cd94-4a01-8f01-0000000e0100");

    /// <summary>The long, wiki-typed fixture — the half of the back-to-top contract that scrolls.</summary>
    public const string LongPageSegment = "long-page";

    /// <summary>The fixture that fits the viewport, where the back-to-top link must never reveal.</summary>
    public const string ShortPageSegment = "short-page";

    /// <summary>The fixture that guarantees a site search returns a hit.</summary>
    public const string SearchFixtureSegment = "search-fixture";

    /// <summary>
    /// The term the search fixture carries as a keyword. A single lowercase token so the Postgres
    /// text-search stemmer has nothing to do with it, and a word nothing an editor writes would
    /// contain — the point is that a search for it matches the fixture and only the fixture.
    /// </summary>
    public const string SearchTerm = "testfixture";
}
