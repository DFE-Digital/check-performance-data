namespace DfE.CheckPerformanceData.Application.ContentStaging;

// The library images an export carries, and their files, each file once.
public sealed record ContentBundleMedia(List<MediaAssetBundleItem> Assets, List<MediaFileBundleItem> Files)
{
    public static ContentBundleMedia Empty => new([], []);
}
