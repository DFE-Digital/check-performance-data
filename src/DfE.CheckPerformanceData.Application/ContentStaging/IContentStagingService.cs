namespace DfE.CheckPerformanceData.Application.ContentStaging;

public interface IContentStagingService
{
    // Builds a schema-versioned bundle of all live wiki pages and content blocks (current
    // content only). Header metadata (ExportedAtUtc / ExportedBy) is left for the caller to set.
    Task<ContentBundle> ExportAsync();

    // Replays a bundle through the normal application services (no raw SQL). Existing content
    // is handled per the chosen mode; children whose parent is absent are skipped and reported.
    // Throws ContentImportConflictException in Fail mode if anything in the bundle already exists.
    Task<ContentImportResult> ImportAsync(ContentBundle bundle, ContentImportMode mode);
}
