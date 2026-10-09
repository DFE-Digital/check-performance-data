namespace DfE.CheckPerformanceData.Application.ContentStaging;

// Knows which pages the start-up import (ManifestContentImporter) will overwrite in this
// environment, so the editor can warn somebody before they change one. It reads the same manifest
// and the same bundles as the importer, once, and keeps the answer for the life of the process:
// the files ship with the application and do not change while it runs.
//
// Only files imported with "replace" or "replaceOlder" count. A file imported with "keep" never
// overwrites anything, so its pages are as safe to edit as any other.
//
// The editor has to open whatever state the import folder is in, so anything that cannot be read
// is treated as not there.
public sealed class ImportedContentIndex
{
    public static readonly ImportedContentIndex Empty = new([]);

    private readonly Dictionary<Guid, ImportedPage> _pages;

    private ImportedContentIndex(Dictionary<Guid, ImportedPage> pages) => _pages = pages;

    /// <summary>The import that will overwrite this page, or null if none will.</summary>
    public ImportedPage? Find(Guid pageId) => _pages.GetValueOrDefault(pageId);

    public static ImportedContentIndex Load(string folder, string environment)
    {
        var pages = new Dictionary<Guid, ImportedPage>();

        ContentImportManifest? manifest;
        try
        {
            manifest = ContentImportManifest.Read(folder);
        }
        catch (Exception)
        {
            return Empty;
        }

        foreach (var entry in manifest?.Imports ?? [])
        {
            if (entry is null || !entry.IsFor(environment) || !entry.NamesAFileIn(folder))
                continue;

            var existing = entry.ExistingPolicy;
            if (existing is not ("replace" or "replaceolder"))
                continue;

            try
            {
                var bundle = ContentStagingJson.Deserialize(File.ReadAllText(Path.Combine(folder, entry.File)));
                foreach (var page in bundle?.PageNodes ?? [])
                    pages[page.Id] = new ImportedPage(entry.File, ReplacedOnEveryStart: existing == "replace");
            }
            catch (Exception)
            {
                // The importer logs a file it cannot read. Here it only means nothing to warn about.
            }
        }

        return new ImportedContentIndex(pages);
    }
}

/// <param name="File">The file in the import folder that the page comes from.</param>
/// <param name="ReplacedOnEveryStart">
/// True when the page is put back as shipped each time the application starts; false when it is
/// replaced only once a newer file is released.
/// </param>
public sealed record ImportedPage(string File, bool ReplacedOnEveryStart);
