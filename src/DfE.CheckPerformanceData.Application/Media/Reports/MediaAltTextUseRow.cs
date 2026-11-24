using DfE.CheckPerformanceData.Application.Media.Embedded;

namespace DfE.CheckPerformanceData.Application.Media.Reports;

/// <summary>
/// One place where readers get missing or weak alt text for a library image: on a page (with the
/// current versions that show it) or in a content block. <see cref="Alt"/> is the text readers
/// get. <see cref="FromLibrary"/> is true when the page or block gives no text of its own, so the
/// image's default text shows and is fixed on the image's page. Otherwise it is fixed in the page
/// or block editor.
/// </summary>
public sealed record MediaAltTextUseRow(
    Guid AssetId,
    string FileName,
    Guid? PageNodeId,
    string? BlockKey,
    string OwnerName,
    IReadOnlyList<EmbeddedVersionState> States,
    string Location,
    string? Alt,
    bool FromLibrary,
    AltTextProblem Problem);
