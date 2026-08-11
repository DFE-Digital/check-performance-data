using System.Reflection;
using DfE.CheckPerformanceData.Application.ContentStaging;

namespace DfE.CheckPerformanceData.Application.PageTree;

// Loads the sample CMS content shipped with the application. The content is a content-staging
// bundle embedded in this assembly rather than pages assembled in C#, so what a developer gets
// after seeding is exactly what the CMS produced when the bundle was exported — there is no
// second, hand-written notion of what a page looks like that can drift from the editor's output.
//
// The file carries the sample pages only. Their parents — the four root nodes and /help/not-found
// — are created at start-up by DefaultPageNodeSeeder and are deliberately absent here, so seeding
// never competes with that seeder for ownership of the roots. Parentage is by the pinned root
// Guids in DefaultPageNodeRoots, which is what lets a static file resolve against a fresh
// database.
//
// To change the samples: seed an environment, edit the pages through the CMS, export a bundle
// from /admin/content-staging, strip the roots and /help/not-found, and replace this file. Editing
// the JSON by hand works but bypasses the guarantee above — the point of the format is that the
// application wrote it.
public static class SampleContentSeedBundle
{
    // Suffix rather than the full manifest name: the resource is prefixed with the assembly's
    // root namespace and its folder path, and pinning the whole string here would break on a
    // folder rename with a runtime error rather than a compile error.
    private const string ResourceSuffix = "SeedContent.sample-content.json";

    public static ContentBundle Load()
    {
        var assembly = typeof(SampleContentSeedBundle).Assembly;
        var name = assembly.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"The sample content bundle is missing from {assembly.GetName().Name}. It must be declared as an " +
                $"EmbeddedResource whose path ends with '{ResourceSuffix}'.");

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        return ContentStagingJson.Deserialize(json)
            ?? throw new InvalidOperationException(
                "The sample content bundle is present but could not be parsed as a content-staging bundle.");
    }
}
