namespace DfE.CheckPerformanceData.Application.ContentStaging;

// The library image version a page version showed when it was saved. Viewing that page version
// in its history shows the same file, so the pin travels with the version. Version is the number
// in the exporting environment; import maps it to the matching version in the target.
public sealed record MediaPinBundleItem
{
    public Guid MediaId { get; init; }
    public int Version { get; init; }
}
