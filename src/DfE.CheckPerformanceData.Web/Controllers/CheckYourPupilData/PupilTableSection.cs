using DfE.CheckPerformanceData.Application.CheckYourPupilData.Columns;

namespace DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;

/// <summary>
/// One searchable, paged pupils table on the check-your-data page. KS4 renders its two sections
/// as tabs (the tab axis is inclusion status); Post16 stacks both inside a single "Pupils" tab
/// (there, the tab axis will be dataset, once the other 16-19 import files arrive).
///
/// <see cref="Key"/> is the query-string prefix, so a section's page and search parameters are
/// <c>{Key}Page</c> and <c>{Key}Search</c>. Adding a third section needs no new named parameters.
/// </summary>
public sealed class PupilTableSection
{
    public required string Key { get; init; }
    public required string TabLabel { get; init; }
    public required string Heading { get; init; }
    public required string DownloadAction { get; init; }
    public required string DownloadLinkText { get; init; }
    public required string EmptyContentKey { get; init; }
    public required string EmptyContentHtml { get; init; }
    public required PupilTable Table { get; init; }
    public required int Page { get; init; }
    public required int TotalPages { get; init; }
    public string? Search { get; init; }

    public string PageParam => $"{Key}Page";
    public string SearchParam => $"{Key}Search";
    public bool HasData => TotalPages > 0;
}

/// <summary>The arguments <c>_PupilSection.cshtml</c> needs to render one section.</summary>
public sealed class PupilSectionViewModel
{
    public required string WindowId { get; init; }
    public required PupilTableSection Section { get; init; }
    public required IReadOnlyList<PupilTableSection> AllSections { get; init; }

    /// <summary>The tab this section is rendered inside; used as the return anchor.</summary>
    public string? TabAnchor { get; init; }
}
