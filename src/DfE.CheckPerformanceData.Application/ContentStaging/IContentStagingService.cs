namespace DfE.CheckPerformanceData.Application.ContentStaging;

public interface IContentStagingService
{
    // Builds a schema-versioned bundle of wiki pages and content blocks (current content only).
    // A null selection exports everything; otherwise only the selected items (plus ancestors of
    // selected pages). Header metadata (ExportedAtUtc / ExportedBy) is left for the caller to set.
    Task<ContentBundle> ExportAsync(ContentExportSelection? selection = null);

    // The catalogue of exportable content (pages + blocks with metadata) for the selection UI.
    Task<ContentCatalog> GetCatalogAsync();

    // Replays a bundle through the normal application services (no raw SQL). Existing content
    // is handled per the chosen mode; children whose parent is absent are skipped and reported.
    // Throws ContentImportConflictException in Fail mode if anything in the bundle already exists.
    Task<ContentImportResult> ImportAsync(ContentBundle bundle, ContentImportMode mode);
}
