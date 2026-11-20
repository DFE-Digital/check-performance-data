namespace DfE.CheckPerformanceData.Application.Media.Upload;

/// <summary>Either the cleaned file, or why it was refused, worded for the editor who uploaded it.</summary>
public sealed record MediaCheckResult(CleanedMediaFile? File, string? Rejection)
{
    public bool IsAccepted => File is not null;

    public static MediaCheckResult Accept(CleanedMediaFile file) => new(file, null);

    public static MediaCheckResult Reject(string reason) => new(null, reason);
}
