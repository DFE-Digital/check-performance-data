using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.Media.Upload;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

/// <summary>
/// What happened to one uploaded file. The page shows it after a plain form post, and the upload
/// script receives it as JSON. DetailUrl is the new image, or for a duplicate the image that
/// already holds the same file. Rejection is the reason to show when the file was refused.
/// </summary>
public sealed record MediaUploadItemViewModel(
    [property: JsonIgnore] MediaUploadOutcome Outcome,
    string FileName,
    string? DetailUrl,
    string? ThumbnailUrl,
    string? ExistingFileName,
    string? Rejection)
{
    /// <summary>"created", "duplicate" or "rejected".</summary>
    public string OutcomeName => Outcome.ToString().ToLowerInvariant();
}
