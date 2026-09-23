using System.ComponentModel.DataAnnotations;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

/// <summary>The Add and Edit exercise form (#466). Kind is shown, never posted — it is immutable.</summary>
public sealed class ExerciseFormItem : AdminPage
{
    public new Guid WindowId { get; set; }
    public string Heading { get; set; } = string.Empty;
    /// <summary>"Pupil data checking", "Results enquiry" or <see cref="CheckingExerciseNames.DisplayOnlyLabel"/>.
    /// Null on Add.</summary>
    public string? KindLabel { get; set; }

    [Required(ErrorMessage = "Enter a name")]
    // Wording matches IWindowService's own "200 characters or less" exactly — two spellings of the
    // same error would be a defect. Kept a literal because StringLength requires a const expression;
    // see ExerciseDefinition.MaxNameLength for the value this must track.
    [StringLength(200, ErrorMessage = "Name must be 200 characters or less")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a tab name")]
    // See ExerciseDefinition.MaxTabNameLength for the value this must track.
    [StringLength(100, ErrorMessage = "Tab name must be 100 characters or less")]
    public string TabName { get; set; } = string.Empty;

    [Range(0, 999, ErrorMessage = "Sort order must be between 0 and 999")]
    public int SortOrder { get; set; }

    [Required(ErrorMessage = "Start date can not be empty")]
    public DateTime? StartDate { get; set; }
    [Range(0, 23, ErrorMessage = "Start hour must be between 0 and 23")]
    public int StartHour { get; set; }
    [Range(0, 59, ErrorMessage = "Start minute must be between 0 and 59")]
    public int StartMinute { get; set; }

    [Required(ErrorMessage = "End date can not be empty")]
    public DateTime? EndDate { get; set; }
    [Range(0, 23, ErrorMessage = "End hour must be between 0 and 23")]
    public int EndHour { get; set; }
    [Range(0, 59, ErrorMessage = "End minute must be between 0 and 59")]
    public int EndMinute { get; set; }

    public DateTime? StartDateTime => StartDate?.Date.AddHours(StartHour).AddMinutes(StartMinute);
    public DateTime? EndDateTime => EndDate?.Date.AddHours(EndHour).AddMinutes(EndMinute);
}
