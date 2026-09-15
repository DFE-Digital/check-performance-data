using System.ComponentModel.DataAnnotations;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

public sealed class CreateCheckingExerciseItem : AdminPage, IValidatableObject
{
    [BindNever]
    public CheckingExerciseDto? DataExercise { get; set; }

    [BindNever]
    public bool IsEditing { get; set; }

    [BindNever]
    public bool TabNameOptional { get; set; }

    public string PageTitle => IsEditing ? "Edit checking exercise" : "Add checking exercise";
    public string SubmitLabel => IsEditing ? "Save changes" : "Add checking exercise";

    [BindNever]
    public string WindowTitle { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter an exercise name")]
    [StringLength(200, ErrorMessage = "Exercise name must be 200 characters or fewer")]
    public string? Name { get; set; }

    [Required(ErrorMessage = "Select an exercise type")]
    [EnumDataType(typeof(CheckingExerciseType), ErrorMessage = "Select a valid exercise type")]
    public CheckingExerciseType? ExerciseType { get; set; }

    [StringLength(100, ErrorMessage = "Tab name must be 100 characters or fewer")]
    public string? TabName { get; set; }

    [Required(ErrorMessage = "Enter a tab order")]
    [Range(0, int.MaxValue, ErrorMessage = "Tab order must be 0 or more")]
    public int? TabOrder { get; set; } = 0;

    [Required(ErrorMessage = "Enter a display order")]
    [Range(0, int.MaxValue, ErrorMessage = "Display order must be 0 or more")]
    public int? SortOrder { get; set; } = 0;

    public bool IsEnabled { get; set; }
    public DateTime? VisibleFrom { get; set; }
    public DateTime? VisibleUntil { get; set; }
    public Guid? ReplacesCheckingExerciseId { get; set; }

    public ExerciseDatesItem Dates { get; set; } = new()
    {
        StartHour = ExerciseDatesItem.DefaultStartHour,
        EndHour = ExerciseDatesItem.DefaultEndHour
    };

    [BindNever]
    public IReadOnlyList<CheckingExerciseDto> ReplacementOptions { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Dates.StartHour is >= 0 and <= 23 && Dates.StartMinute is >= 0 and <= 59
            && Dates.EndHour is >= 0 and <= 23 && Dates.EndMinute is >= 0 and <= 59
            && Dates.EndDateTime < Dates.StartDateTime)
            yield return new ValidationResult("End date cannot occur before the start date", ["Dates.EndDate"]);

        if (VisibleFrom is not null && VisibleUntil <= VisibleFrom)
            yield return new ValidationResult("Visible until must be after visible from", [nameof(VisibleUntil)]);
    }
}
