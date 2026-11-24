namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>
/// A page that uses a library image in any of its versions. A page that is deleted (but not
/// removed for good) still counts: its versions keep their usages, so the image cannot be deleted.
/// <see cref="InCurrentVersion"/> is true when the page's live, scheduled or draft version uses
/// the image, and false when only older versions of the page do.
/// </summary>
public sealed record MediaUsagePage(Guid PageNodeId, string Title, string Path, bool IsDeleted, bool InCurrentVersion);
