namespace DfE.CheckPerformanceData.Application.ContentStaging;

public interface IContentStagingService
{
    // Builds a schema-versioned bundle of wiki pages and content blocks (current content only).
    // A null selection exports everything; otherwise only the selected items (plus ancestors of
    // selected pages). Header metadata (ExportedAtUtc / ExportedBy) is left for the caller to set.
    Task<ContentBundle> ExportAsync(ContentExportSelection? selection = null);

    // The catalogue of exportable content (pages + blocks with metadata) for the selection UI.
    Task<ContentCatalog> GetCatalogAsync();

    // A dry-run analysis of a bundle against the current environment: which items are new and
    // which collide with existing content. Shown on the import preview page; makes no changes.
    Task<ContentImportPreview> PreviewAsync(ContentBundle bundle);

    // Replays a bundle through the normal application services (no raw SQL). Each item is
    // resolved to an effective mode using: the explicit per-item decision if given, otherwise
    // the collision default (for items that already exist) or the new-item default (for items
    // that don't). Children whose parent is absent are skipped and reported. Throws
    // ContentImportConflictException if a collision is left at the Fail mode.
    //
    // newItemMode is optional to preserve older call sites; when omitted it defaults to Replace,
    // which is the historical behaviour ("always create new items").
    Task<ContentImportResult> ImportAsync(
        ContentBundle bundle,
        ContentImportMode mode,
        IReadOnlyDictionary<Guid, ContentImportMode>? decisions = null,
        ContentImportMode newItemMode = ContentImportMode.Replace);
}
