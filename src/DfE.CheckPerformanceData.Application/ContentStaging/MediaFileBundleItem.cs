namespace DfE.CheckPerformanceData.Application.ContentStaging;

// One stored media file, carried once however many image versions share it. Sha256 is the
// lower-case hex SHA-256 of Data as exported; import checks it against the bytes before using
// them. Data is written as base64 in the bundle JSON.
public sealed record MediaFileBundleItem
{
    private readonly string _sha256 = string.Empty;
    public string Sha256 { get => _sha256; init => _sha256 = BundleMemberDefaults.OrEmpty(value); }

    private readonly byte[] _data = [];
    public byte[] Data { get => _data; init => _data = value ?? []; }
}
