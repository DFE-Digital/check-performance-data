using System.Reflection;
using DfE.CheckPerformanceData.Application.ContentStaging;

namespace DfE.CheckPerformanceData.Application.PageTree;

// Loads the content the automated browser tests navigate to. Same format and same rationale as
// SampleContentSeedBundle — a content-staging bundle the CMS exported, rather than pages assembled
// in C# that can drift from what an editor's output actually looks like.
//
// It is a separate bundle from the sample content because the two have different owners. Sample
// content is demonstration material: an editor may reasonably edit it, and the sample seed leaves
// anything already present alone so that pressing the button never destroys their work. Fixtures
// belong to the test suite. They sit under their own root so they are never mistaken for content,
// and they are re-imported over the top on every seed — which is the only thing that can bring back
// a page whose versions were deleted. A page emptied that way still exists, so an import that skips
// on collision walks straight past it and the route 404s for good.
//
// The root itself is deliberately absent, exactly as the four default roots are absent from the
// sample bundle: it is created by TestFixturePageNodeSeeder, and parentage here is by its pinned
// Guid, which is what lets a static file resolve against any environment's database.
//
// To change a fixture: seed an environment, edit the page through the CMS, export a bundle from
// /admin/content-staging, strip the root, and replace this file.
public static class TestFixtureSeedBundle
{
    // Suffix rather than the full manifest name, for the same reason as the sample bundle: the
    // resource name carries the assembly's root namespace and folder path, and pinning the whole
    // string would turn a folder rename into a runtime error instead of a compile error.
    private const string ResourceSuffix = "SeedContent.test-fixture-content.json";

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

    public static ContentBundle Load()
    {
        var assembly = typeof(TestFixtureSeedBundle).Assembly;
        var name = assembly.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"The test-fixture content bundle is missing from {assembly.GetName().Name}. It must be declared as an " +
                $"EmbeddedResource whose path ends with '{ResourceSuffix}'.");

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        return ContentStagingJson.Deserialize(json)
            ?? throw new InvalidOperationException(
                "The test-fixture content bundle is present but could not be parsed as a content-staging bundle.");
    }
}
