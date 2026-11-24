using System.ComponentModel.DataAnnotations;
using DfE.CheckPerformanceData.Application.Media;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

/// <summary>
/// The "Image details" form on an image's page. Tags are typed as one comma-separated line.
/// The text limits match the database columns, so a valid form always saves. The repository
/// trims the values, stores blank ones as null and removes repeated tags.
/// </summary>
public sealed class MediaMetadataForm : IValidatableObject
{
    public const int MaxTags = 20;
    public const int MaxTagLength = 100;

    [StringLength(1000, ErrorMessage = "Alt text must be 1000 characters or fewer")]
    public string? AltText { get; set; }

    [StringLength(2000, ErrorMessage = "Caption must be 2000 characters or fewer")]
    public string? Caption { get; set; }

    [StringLength(500, ErrorMessage = "Credit must be 500 characters or fewer")]
    public string? Credit { get; set; }

    public string? Tags { get; set; }

    public static MediaMetadataForm From(MediaAssetDto asset) => new()
    {
        AltText = asset.DefaultAltText,
        Caption = asset.Caption,
        Credit = asset.Credit,
        Tags = string.Join(", ", asset.Tags),
    };

    public MediaAssetMetadata ToMetadata() => new(AltText, Caption, Credit, SplitTags());

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var tags = SplitTags();
        if (tags.Count > MaxTags)
            yield return new ValidationResult($"Enter {MaxTags} tags or fewer", [nameof(Tags)]);
        else if (tags.Any(t => t.Length > MaxTagLength))
            yield return new ValidationResult($"Each tag must be {MaxTagLength} characters or fewer", [nameof(Tags)]);
    }

    private List<string> SplitTags() =>
        (Tags ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
}
