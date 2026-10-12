using System.ComponentModel.DataAnnotations;
using System.Globalization;
using GovUk.Frontend.AspNetCore;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

/// <summary>
/// AB#307773: the add and edit page of one extension of a checking exercise — a later end date for
/// the schools it names. The end date and time are the members <see cref="ExerciseDatesItem"/> has
/// for an exercise's own end, so the two pages ask for a date the same way.
/// </summary>
/// <remarks>
/// Not an <see cref="AdminPage"/>: its WindowId is init-only and its urls are set by the
/// controller, whereas here the ids come from the route on every POST and the urls follow from them.
/// </remarks>
public sealed class ExerciseExtensionItem
{
    public Guid WindowId { get; set; }
    public Guid ExerciseId { get; set; }

    /// <summary>Null on add.</summary>
    public Guid? ExtensionId { get; set; }

    /// <summary>The exercise's name, as on its summary card: several releases may share a kind.</summary>
    public string ExerciseLabel { get; set; } = string.Empty;

    /// <summary>The exercise's own end, which the extension must follow. Shown above the date.</summary>
    public DateTime ExerciseEnd { get; set; }

    /// <summary>
    /// Edit only: the end date as the page showed it, posted back in a hidden field. The save is a
    /// compare-and-set on it, so an edit never overwrites a change the admin did not see.
    /// </summary>
    public DateTime? PreviousEndDate { get; set; }

    [Required(ErrorMessage = "Enter the extension end date")]
    [DateInput(ErrorMessagePrefix = "Extension end date")]
    public DateTime? EndDate { get; set; }

    [Range(0, 23, ErrorMessage = "End hour must be between 0 and 23")]
    public int EndHour { get; set; } = ExerciseDatesItem.DefaultEndHour;

    [Range(0, 59, ErrorMessage = "End minute must be between 0 and 59")]
    public int EndMinute { get; set; }

    public DateTime? EndDateTime => EndDate?.Date.AddHours(EndHour).AddMinutes(EndMinute);

    /// <summary>One LAESTAB per line, as typed; shown back on error.</summary>
    public string? Schools { get; set; }

    /// <summary>Schools with no file in the current release, slashed; the admin confirms to save anyway.</summary>
    public IReadOnlyList<string> SchoolsWithoutData { get; set; } = [];

    /// <summary>The exercise has no release yet, so no school named has a file.</summary>
    public bool ExerciseHasNoRelease { get; set; }

    /// <summary>Ticked on the warning: save even though some schools have no data file.</summary>
    public bool ConfirmNoData { get; set; }

    public bool IsEdit => ExtensionId is not null;
    public bool HasWarning => SchoolsWithoutData.Count > 0 || ExerciseHasNoRelease;

    public string Heading => IsEdit ? "Edit extension" : $"Add an extension to {ExerciseLabel}";
    public string ConfirmLabel => IsEdit ? "Save these schools anyway" : "Add these schools anyway";

    // Invariant culture: "/" in a custom format is the culture's date separator.
    public string ExerciseEndText => ExerciseEnd.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    // Round-trip form: the compare-and-set needs the exact value, and ISO 8601 binds back in any culture.
    public string? PreviousEndDateValue => PreviousEndDate?.ToString("o", CultureInfo.InvariantCulture);

    public string PostUrl => IsEdit
        ? $"/admin/windows/{WindowId}/exercises/{ExerciseId}/extensions/{ExtensionId}/edit"
        : $"/admin/windows/{WindowId}/exercises/{ExerciseId}/extensions/new";

    public string CancelLink => $"/admin/windows/summary/{WindowId}";
}
