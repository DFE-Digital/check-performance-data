namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>
/// The order of the library list. Newest puts the most recently changed image first, so a new
/// upload or a replaced file is at the top. Name sorts by file name, ignoring case.
/// </summary>
public enum MediaAssetSort
{
    Newest,
    Name
}
