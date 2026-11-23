namespace DfE.CheckPerformanceData.Application.ContentStaging;

// One folder on an image's folder path, from the top of the library down. Import finds the folder
// by its name under the folder before it (ignoring case). Only a folder that is not there is made,
// and it keeps this id when the id is free.
public sealed record MediaFolderBundleItem
{
    public Guid Id { get; init; }

    private readonly string _name = string.Empty;
    public string Name { get => _name; init => _name = BundleMemberDefaults.OrEmpty(value); }
}
