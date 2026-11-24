namespace DfE.CheckPerformanceData.Application.Media.Upload;

public enum MediaUploadOutcome
{
    Created,
    Duplicate,
    Rejected
}

/// <summary>
/// The outcome of an upload. Asset is the new image when Created, or the image that already holds
/// the same file when Duplicate. Rejection is the reason to show the editor when Rejected.
/// </summary>
public sealed record MediaUploadResult(MediaUploadOutcome Outcome, MediaAssetDto? Asset, string? Rejection)
{
    public static MediaUploadResult Created(MediaAssetDto asset) => new(MediaUploadOutcome.Created, asset, null);

    public static MediaUploadResult Duplicate(MediaAssetDto existing) => new(MediaUploadOutcome.Duplicate, existing, null);

    public static MediaUploadResult Rejected(string reason) => new(MediaUploadOutcome.Rejected, null, reason);
}
