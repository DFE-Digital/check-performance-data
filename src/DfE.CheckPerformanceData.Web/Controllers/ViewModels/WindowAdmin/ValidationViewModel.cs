using DfE.CheckPerformanceData.Infrastructure.Ingress;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

public class ValidationViewModel : AdminPage
{
    public string? StreamUrl { get; set; }

    /// <summary>Which checking exercise this run belongs to, e.g. "Pupil data checking" (#319).</summary>
    public string ExerciseLabel { get; set; } = string.Empty;

    public ProcessingResult? ProcessingResult { get; set; }

    /// <summary>True only on the exercise-id addressed route (#466 slice 2/3), which is the only
    /// one <see cref="Controllers.WindowAdmin.ValidateWindowController"/> passes a
    /// <c>clearExistingFiles</c> flag through to <c>ICheckingExerciseIngress</c>. The kind-addressed
    /// route's own run has never supported wiping previous output, so the checkbox would do
    /// nothing there and must not be shown.</summary>
    public bool ShowClearExistingFiles { get; set; }

    private bool ValidateOnly { get; set; } = true;
}
