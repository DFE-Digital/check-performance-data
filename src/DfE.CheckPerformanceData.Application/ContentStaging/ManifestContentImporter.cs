using System.Text.Json;
using DfE.CheckPerformanceData.Application.PageTree;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.ContentStaging;

// Imports the content the service ships with. At start-up, in every environment, it reads
// manifest.json from the import folder and imports each content-staging bundle the manifest
// lists, in the order listed. Content that every environment needs — the service's own help
// pages, pages the automated tests navigate to — is then put in place by a deployment, and
// nobody has to remember to import it environment by environment.
//
// The manifest is a list of files. Each says which environments it is imported into and what to
// do about content that is already there:
//
//   {
//     "imports": [
//       {
//         "file": "cms-guide.json",
//         "environments": [ "Development", "Review", "QA", "Preproduction", "Production" ],
//         "existing": "replaceOlder"
//       },
//       { "file": "test-pages.json", "environments": [ "Development", "Review" ], "existing": "replace" }
//     ]
//   }
//
//   environments     the environments to import the file into, by name (ASPNETCORE_ENVIRONMENT).
//                    A file is imported nowhere it is not named, so a new environment gets
//                    nothing until somebody decides it should.
//   existing         keep (the default)  add what is missing and leave everything else alone.
//                    replace             overwrite what is there, every time. For content nobody
//                                        edits, which must always be exactly as shipped.
//                    replaceOlder        overwrite a page only if it was last changed before the
//                                        file was issued (the file's exportedAtUtc). A newer file
//                                        replaces older pages; a page edited since is kept until
//                                        a newer file is released. Replacing a page stamps it
//                                        with the time of the import, so the next start-up finds
//                                        nothing to do. Content blocks are added if missing and
//                                        otherwise kept.
//
// Each file goes through the ordinary importer, so the same validation and sanitisation apply
// as to a bundle an administrator uploads. Whatever the entry says, a page that has been deleted
// stays deleted, along with anything the file would put beneath it; it can be brought back from
// Deleted pages.
//
// This is shipped content, not the service itself, and the service has to start without it. A
// file that is missing, unreadable or fails to import is logged and skipped, and the files after
// it are still tried.
public sealed class ManifestContentImporter(
    IPageNodeRepository pageNodeRepository,
    IContentStagingService staging,
    ILogger<ManifestContentImporter> logger)
{
    public const string ManifestFileName = "manifest.json";

    public async Task<ManifestImportSummary> RunAsync(string folder, string environment)
    {
        var summary = new ManifestImportSummary();

        ContentImportManifest? manifest;
        try
        {
            manifest = ContentImportManifest.Read(folder);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Content import: {Manifest} in {Folder} could not be read, nothing was imported", ManifestFileName, folder);
            summary.FilesFailed++;
            return summary;
        }

        if (manifest is null)
        {
            logger.LogInformation("Content import: no {Manifest} in {Folder}, nothing to import", ManifestFileName, folder);
            return summary;
        }

        foreach (var entry in manifest.Imports ?? [])
        {
            if (entry is null)
                continue;

            // An entry that names no environment is a mistake, not a file for nowhere: say so.
            if (entry.Environments is not { Count: > 0 })
            {
                logger.LogError("Content import: {File} was not imported, because the manifest does not say which environments it is for", entry.File);
                summary.FilesFailed++;
                continue;
            }

            if (!entry.IsFor(environment))
                continue;

            try
            {
                var result = await ImportAsync(folder, entry);
                foreach (var error in result.Errors)
                    logger.LogWarning("Content import: {File}: {Error}", entry.File, error);

                var changed = result.PageNodesCreated + result.PageNodesUpdated;
                logger.LogInformation(
                    "Content import: {File}: {Created} page(s) created, {Updated} replaced, {Skipped} left as they were",
                    entry.File, result.PageNodesCreated, result.PageNodesUpdated, result.PageNodesSkipped);

                summary.FilesImported++;
                summary.PagesChanged += changed;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Content import: {File} was not imported", entry.File);
                summary.FilesFailed++;
            }
        }

        return summary;
    }

    private async Task<ContentImportResult> ImportAsync(string folder, ContentImportManifestEntry entry)
    {
        if (!entry.NamesAFileIn(folder))
            throw new InvalidOperationException($"'{entry.File}' is not the name of a file in the import folder.");

        var existing = entry.ExistingPolicy;
        if (existing is not ("keep" or "replace" or "replaceolder"))
            throw new InvalidOperationException($"'{entry.Existing}' is not a value of 'existing'. Use keep, replace or replaceOlder.");

        var bundle = ContentStagingJson.Deserialize(await File.ReadAllTextAsync(Path.Combine(folder, entry.File)))
            ?? throw new InvalidOperationException("The file is empty.");

        var decisions = await WithholdDeletedAsync(bundle);
        if (existing == "replaceolder")
            await ReplaceOlderAsync(bundle, decisions);

        return await staging.ImportAsync(
            bundle,
            mode: existing == "replace" ? ContentImportMode.Replace : ContentImportMode.Skip,
            decisions: decisions,
            newItemMode: ContentImportMode.Replace);
    }

    // Deleted itself, or beneath a page in the file that is.
    private async Task<Dictionary<Guid, ContentImportMode>> WithholdDeletedAsync(ContentBundle bundle)
    {
        var decisions = new Dictionary<Guid, ContentImportMode>();
        var deleted = (await pageNodeRepository.GetDeletedAsync()).Select(n => n.Id).ToHashSet();
        if (deleted.Count == 0)
            return decisions;

        var byId = bundle.PageNodes.ToDictionary(p => p.Id);
        foreach (var page in bundle.PageNodes)
        {
            for (var p = page; p is not null; p = p.ParentId is { } id && byId.TryGetValue(id, out var parent) ? parent : null)
            {
                if (!deleted.Contains(p.Id))
                    continue;
                decisions[page.Id] = ContentImportMode.Skip;
                break;
            }
        }

        return decisions;
    }

    private async Task ReplaceOlderAsync(ContentBundle bundle, Dictionary<Guid, ContentImportMode> decisions)
    {
        var issued = bundle.ExportedAtUtc
            ?? throw new InvalidOperationException("replaceOlder needs the file to say when it was issued (exportedAtUtc).");

        foreach (var page in bundle.PageNodes)
        {
            if (decisions.ContainsKey(page.Id) || await pageNodeRepository.GetByIdAsync(page.Id) is null)
                continue;

            var versions = await pageNodeRepository.GetVersionsAsync(page.Id);
            var lastChanged = versions.Count == 0 ? DateTime.MinValue : versions.Max(v => v.UpdatedDate);
            if (lastChanged < issued)
                decisions[page.Id] = ContentImportMode.Replace;
        }
    }
}

// manifest.json, as read from the import folder.
public sealed class ContentImportManifest
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public List<ContentImportManifestEntry> Imports { get; init; } = [];

    /// <summary>The manifest in a folder, or null when the folder has none. Throws if it cannot be read.</summary>
    public static ContentImportManifest? Read(string folder)
    {
        var path = Path.Combine(folder, ManifestContentImporter.ManifestFileName);
        if (!File.Exists(path))
            return null;

        return JsonSerializer.Deserialize<ContentImportManifest>(File.ReadAllText(path), Options) ?? new ContentImportManifest();
    }
}

public sealed class ContentImportManifestEntry
{
    public string File { get; init; } = string.Empty;

    public List<string>? Environments { get; init; }

    public string? Existing { get; init; }

    /// <summary>The value of <see cref="Existing"/> in lower case, "keep" when it is left out.</summary>
    public string ExistingPolicy => (Existing ?? "keep").Trim().ToLowerInvariant();

    public bool IsFor(string environment) =>
        Environments is not null && Environments.Contains(environment, StringComparer.OrdinalIgnoreCase);

    // A file name and nothing else: the manifest decides what reaches production, so it must
    // not be able to name anything outside its own folder.
    public bool NamesAFileIn(string folder) =>
        !string.IsNullOrWhiteSpace(File)
        && File == Path.GetFileName(File)
        && !File.Contains("..")
        && System.IO.File.Exists(Path.Combine(folder, File));
}

// What a run did, for the log and for tests.
public sealed class ManifestImportSummary
{
    public int FilesImported { get; set; }

    public int FilesFailed { get; set; }

    public int PagesChanged { get; set; }
}
