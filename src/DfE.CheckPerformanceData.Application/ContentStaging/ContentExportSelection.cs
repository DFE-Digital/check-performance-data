namespace DfE.CheckPerformanceData.Application.ContentStaging;

// The subset of content an administrator chose to export, identified by stable GUID. A null
// selection (the default) exports everything; when supplied, only the listed pages/blocks are
// exported — plus the ancestors of any selected page, so the imported hierarchy stays intact.
public sealed record ContentExportSelection(
    IReadOnlySet<Guid> WikiPageIds,
    IReadOnlySet<Guid> ContentBlockIds);
