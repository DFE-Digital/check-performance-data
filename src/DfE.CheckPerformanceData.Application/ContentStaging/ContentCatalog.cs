namespace DfE.CheckPerformanceData.Application.ContentStaging;

// The list of exportable content shown on the selection page, so an administrator can choose
// what goes into an export. Pages are in tree order with a Depth for indentation.
public sealed record ContentCatalog(
    IReadOnlyList<CatalogPage> Pages,
    IReadOnlyList<CatalogBlock> Blocks);

public sealed record CatalogPage(
    Guid Id,
    string Title,
    string SlugPath,
    int Depth,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CatalogBlock(
    Guid Id,
    string Key,
    string BlockType,
    string? LastSeenPath,
    DateTime CreatedAt,
    DateTime UpdatedAt);
