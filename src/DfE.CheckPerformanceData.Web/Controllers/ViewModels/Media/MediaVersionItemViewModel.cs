namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

/// <summary>One row of an image's version history.</summary>
public sealed record MediaVersionItemViewModel(
    int Version,
    string ThumbnailUrl,
    bool IsShownAsStored,
    DateTime UploadedAtUtc,
    string? UploadedBy,
    string? Dimensions,
    string Size,
    bool IsCurrent);
