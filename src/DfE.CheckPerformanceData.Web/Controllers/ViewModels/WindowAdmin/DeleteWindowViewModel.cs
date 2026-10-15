using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

/// <summary>The "are you sure" page before a window is deleted.</summary>
public sealed class DeleteWindowViewModel
{
    public required Guid WindowId { get; init; }
    public required string WindowTitle { get; init; }
    public required int ExerciseCount { get; init; }
    public required DeleteWindowPreview Preview { get; init; }

    /// <summary>
    /// The admin ticks this to say they know the window's change requests go too. Only asked when
    /// there are requests: a warning that can be scrolled past is not enough when schools' work is
    /// lost.
    /// </summary>
    public bool ConfirmRequestsDeleted { get; init; }

    public string CancelLink => $"/admin/windows/summary/{WindowId}";
    public string FormAction => $"/admin/windows/{WindowId}/delete";
}
