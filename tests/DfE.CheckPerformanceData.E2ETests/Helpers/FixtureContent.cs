namespace DfE.CheckPerformanceData.E2ETests.Helpers;

// The CMS content this suite navigates to, and the root it hangs off.
//
// These pages are seeded by the application itself — at start-up wherever SeedDevelopmentData is
// set, and behind the "seed sample CMS pages" admin button — under a root nothing else creates.
// That is the whole point of them: the suite used to navigate to sample pages living in the
// ordinary content tree, which an editor was free to empty, and did. A page whose versions are
// deleted still exists, so the sample seed skipped it and the route returned 404 for good.
//
// Values are mirrored from DefaultPageNodeRoots / TestFixtureSeedBundle rather than referenced,
// because this project deliberately takes no project reference on the application — it drives the
// deployed app over HTTP, as a user does. Same convention the pinned /help root was held under. If a value
// here drifts from the source, the browser tests fail on a 404 that names the path, so the
// mismatch is not silent.
public static class FixtureContent
{
    /// <summary>Pinned Guid of the /development-testing root. Parent for runtime-created fixtures.</summary>
    public static readonly Guid RootId = new("00000000-cd94-4a01-8f01-00000000000e");

    public const string RootPath = "/development-testing";

    /// <summary>Long, wiki-typed page — the half of the back-to-top contract that scrolls.</summary>
    public const string LongPagePath = $"{RootPath}/long-page";

    /// <summary>Page that fits the viewport, where the back-to-top link must never reveal.</summary>
    public const string ShortPagePath = $"{RootPath}/short-page";

    /// <summary>
    /// Keyword carried by the search fixture. Searching for it is guaranteed to return a hit,
    /// which is what the admin analytics tests need before they can assert anything about how the
    /// hits are rendered. They used to search for a word that merely happened to be somewhere in
    /// the corpus, and failed the moment an editor changed it.
    /// </summary>
    public const string SearchTerm = "testfixture";
}
