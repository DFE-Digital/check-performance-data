namespace DfE.CheckPerformanceData.Application.Media.Reports;

/// <summary>One library image by size: the file it shows now, and every version it has stored.</summary>
public sealed record MediaSizeAsset(
    Guid Id,
    string FileName,
    string ContentType,
    int CurrentVersion,
    long CurrentBytes,
    int Versions,
    long StoredBytes,
    Guid? FolderId = null);
