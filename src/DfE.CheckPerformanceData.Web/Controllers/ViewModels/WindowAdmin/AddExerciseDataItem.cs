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

    /// <summary>On pupil data checking and a results enquiry: "journey" merges the file into the
    /// data the journey reads; "share" makes it a data share that schools only view and download.</summary>
    [RegularExpression("journey|share", ErrorMessage = "Select what this file is for")]
    public string Use { get; set; } = "journey";

    /// <summary>The admin confirms that a journey file is merged with the exercise's other journey
    /// files. Asked only when there are any.</summary>
    public bool ConfirmJourney { get; set; }

    [BindNever]
    public string ExerciseName { get; set; } = string.Empty;

    /// <summary>The labels of the files in use that already feed the journey. A new journey file is
    /// merged with them.</summary>
    [BindNever]
    public IReadOnlyList<string> ExistingJourneyFiles { get; set; } = [];

    /// <summary>True on a results enquiry, where a journey file's name is shown to schools as each result's source.</summary>
    [BindNever]
    public bool IsResultsEnquiry { get; set; }

    /// <summary>True on pupil data checking, the only exercise whose files hold pupils to include.</summary>
    [BindNever]
    public bool AsksInclusion { get; set; }

    /// <summary>True on pupil data checking and a results enquiry, the exercises where a file may or
    /// may not feed the journey by the admin's choice.</summary>
    [BindNever]
    public bool AsksUse { get; set; }
}
