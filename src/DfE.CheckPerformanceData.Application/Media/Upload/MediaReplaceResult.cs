namespace DfE.CheckPerformanceData.Application.Media.Upload;

public enum MediaReplaceOutcome
{
    Replaced,
    NotFound,
    Rejected
}

/// <summary>The outcome of a replace: the new version, or why there is none.</summary>
public sealed record MediaReplaceResult(MediaReplaceOutcome Outcome, MediaAssetVersionDto? Version, string? Rejection)
{
    public static MediaReplaceResult Replaced(MediaAssetVersionDto version) => new(MediaReplaceOutcome.Replaced, version, null);

    public static MediaReplaceResult NotFound() => new(MediaReplaceOutcome.NotFound, null, null);

    public static MediaReplaceResult Rejected(string reason) => new(MediaReplaceOutcome.Rejected, null, reason);
}
