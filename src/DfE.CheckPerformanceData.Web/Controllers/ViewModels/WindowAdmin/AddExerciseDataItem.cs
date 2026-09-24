using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

public sealed class AddExerciseDataItem : AdminPage
{
    [Required(ErrorMessage = "Enter a name for this data file")]
    [StringLength(50, ErrorMessage = "The name must be 50 characters or fewer")]
    // The name is not part of any blob path: storage uses the dataset id. It is only a segment of
    // the admin URLs that choose the dataset's CSV and schema, so it may be any text that stays one
    // path segment: no slash (an encoded slash is not decoded into the route value) and not only
    // dots (a browser resolves "." and ".." as path steps).
    [RegularExpression(@"(?!\s*\.+\s*$)[^/\\]+", ErrorMessage = "The name must not contain / or \\, and must not be only dots")]
    public string? Name { get; set; }

    public bool Required { get; set; } = true;

    [RegularExpression("file|included|excluded", ErrorMessage = "Select how pupil inclusion is supplied")]
    public string Inclusion { get; set; } = "file";

    public string? SourceFile { get; set; }

    [BindNever]
    public string ExerciseName { get; set; } = string.Empty;

    [BindNever]
    public IReadOnlyList<string> SourceOptions { get; set; } = [];

    /// <summary>True on pupil data checking, the only exercise whose files hold pupils to include.</summary>
    [BindNever]
    public bool AsksInclusion { get; set; }

    /// <summary>True on a results enquiry, the only exercise whose files hold supplier results.</summary>
    [BindNever]
    public bool AsksSource { get; set; }

    /// <summary>True when a file added here is merged into the data the journey reads.</summary>
    [BindNever]
    public bool FeedsJourney { get; set; }
}
