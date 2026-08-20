using System.ComponentModel.DataAnnotations;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

/// <summary>
/// One checking exercise's own date range (#319). Both ends are on a single page: a window with one
/// exercise is then one date page rather than the two the window-level steps used to take, which is
/// what keeps a single-exercise window no harder to create than it was.
/// </summary>
/// <remarks>
/// The window's own StartDate/EndDate is derived from these as the union and is never typed, so
/// there is no window-level date step for these to disagree with.
/// </remarks>
public sealed class ExerciseDatesItem : AdminPage
{
    public CheckingExerciseType ExerciseType { get; set; }

    /// <summary>Human label for the heading, e.g. "Pupil data checking".</summary>
    public string ExerciseLabel { get; set; } = string.Empty;

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

    /// <summary>New exercises open at midnight and close at 17:00; the admin can change both.</summary>
    public const int DefaultStartHour = 0;
    public const int DefaultEndHour = 17;
}
