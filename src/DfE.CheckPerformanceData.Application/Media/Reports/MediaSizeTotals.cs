namespace DfE.CheckPerformanceData.Application.Media.Reports;

/// <summary>
/// The size of the whole library. <see cref="CurrentBytes"/> adds up the file each image shows
/// now. <see cref="StoredBytes"/> adds up every stored version, because a replaced file stays for
/// the page versions that recorded it. Sizes are of the stored (cleaned) files, not the uploads.
/// </summary>
public sealed record MediaSizeTotals(int Images, int Versions, long CurrentBytes, long StoredBytes);
