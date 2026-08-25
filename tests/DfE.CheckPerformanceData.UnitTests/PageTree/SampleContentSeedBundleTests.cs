using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Application.UnitTests.PageTree;

// The sample content shipped with the application is a content-staging bundle rather than pages
// assembled in C#, so these tests guard the properties the loader and the import replay depend
// on. A bundle is data: nothing in the compiler checks it, and a malformed or stale file would
// otherwise surface as an empty page tree on a developer's first run.
public class SampleContentSeedBundleTests
{
    [Fact]
    public void Bundle_IsEmbeddedAndParses()
    {
        var bundle = SampleContentSeedBundle.Load();

        Assert.NotNull(bundle);
        Assert.NotEmpty(bundle.PageNodes);
    }

    // The bundle format is versioned and the importer rejects anything but the current shape.
    // If the schema moves on, this fails immediately and points at the file that needs
    // regenerating — rather than the application quietly seeding nothing.
    [Fact]
    public void Bundle_DeclaresTheSchemaVersionTheImporterAccepts()
    {
        var bundle = SampleContentSeedBundle.Load();

        Assert.Equal(ContentBundle.CurrentSchemaVersion, bundle.SchemaVersion);
    }

    // Every sample hangs off one of the roots that DefaultPageNodeSeeder creates at start-up,
    // referenced by the pinned Guid rather than by path. An import resolves parents by Guid, so a
    // sample parented to anything else would be dropped as an orphan on a fresh database.
    [Fact]
    public void EverySamplePage_IsParentedToAPinnedRoot()
    {
        var bundle = SampleContentSeedBundle.Load();
        var roots = DefaultPageNodeRoots.All.Select(r => r.Id).ToHashSet();

        foreach (var page in bundle.PageNodes)
        {
            Assert.True(page.ParentId.HasValue,
                $"sample '{page.Segment}' has no parent — the roots come from the start-up seeder, not this bundle");
            Assert.True(roots.Contains(page.ParentId!.Value),
                $"sample '{page.Segment}' is parented to {page.ParentId}, which is not one of the pinned roots");
        }
    }

    // Samples exist so a developer or a browser test has real content to look at, which means
    // published content — a draft-only page 404s on the front end.
    [Fact]
    public void EverySamplePage_HasAPublishedVersion()
    {
        var bundle = SampleContentSeedBundle.Load();

        foreach (var page in bundle.PageNodes)
        {
            Assert.True(page.Versions.Any(v => v.PublishFrom is not null),
                $"sample '{page.Segment}' has no published version, so it would 404 after seeding");
        }
    }

    // The wiki render path takes a different code path from content pages (raw HTML through
    // IHtmlRenderingService rather than a widget tree), and it only stays exercised while at
    // least one wiki-typed sample exists.
    [Fact]
    public void Bundle_KeepsAWikiTypedSample_SoTheWikiRenderPathStaysCovered()
    {
        var bundle = SampleContentSeedBundle.Load();

        Assert.Contains(bundle.PageNodes, p => p.PageType == "wiki");
    }

    // Two pages under the same parent with the same segment would collide on path. Nothing in
    // the bundle format prevents it, and the failure at import time is obscure.
    [Fact]
    public void Bundle_HasNoDuplicateSegmentsUnderTheSameParent()
    {
        var bundle = SampleContentSeedBundle.Load();

        var duplicates = bundle.PageNodes
            .GroupBy(p => (p.ParentId, p.Segment.ToLowerInvariant()))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.Item2)
            .ToList();

        Assert.Empty(duplicates);
    }

    // Ids are baked into the committed file on purpose, for the same reason the roots are pinned:
    // every environment seeded from this bundle gets the same page identities, so a later
    // content-staging import updates those rows instead of adopting them via the path fallback.
    [Fact]
    public void EverySamplePage_HasAStableNonEmptyId()
    {
        var bundle = SampleContentSeedBundle.Load();

        Assert.All(bundle.PageNodes, p => Assert.NotEqual(Guid.Empty, p.Id));
        Assert.Equal(bundle.PageNodes.Count, bundle.PageNodes.Select(p => p.Id).Distinct().Count());
    }
}
