using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

public sealed class AddExerciseDataItem : AdminPage
{
    [Required(ErrorMessage = "Enter a name for this data file")]
    [StringLength(50, ErrorMessage = "The name must be 50 characters or fewer")]
    [RegularExpression("[a-z0-9][a-z0-9_-]*", ErrorMessage = "Use lowercase letters, numbers, hyphens or underscores")]
    public string? Name { get; set; }

    public bool Required { get; set; } = true;

    [RegularExpression("file|included|excluded", ErrorMessage = "Select how pupil inclusion is supplied")]
    public string Inclusion { get; set; } = "file";

    public string? SourceFile { get; set; }

    [BindNever]
    public string ExerciseName { get; set; } = string.Empty;

    [BindNever]
    public IReadOnlyList<string> SourceOptions { get; set; } = [];
}
