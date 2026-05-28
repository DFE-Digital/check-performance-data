namespace DfE.CheckPerformanceData.Application.Wiki;

// Outcome of an idempotent create. Created is false when a page with the same slug
// already existed under the same parent (it was returned untouched), true when a new
// page was inserted. Lets callers (e.g. the sample-page seeder) report how many pages
// were actually added without re-deriving slugs themselves.
public sealed record WikiPageCreationResult(WikiPageDto Page, bool Created);
