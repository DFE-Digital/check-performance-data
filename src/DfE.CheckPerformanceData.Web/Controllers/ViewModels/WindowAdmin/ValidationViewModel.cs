using DfE.CheckPerformanceData.Infrastructure.Ingress;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

public class ValidationViewModel : AdminPage
{
    public string? StreamUrl { get; set; }

    /// <summary>Which checking exercise this run belongs to, e.g. "Pupil data checking" (#319).</summary>
    public string ExerciseLabel { get; set; } = string.Empty;

    public ProcessingResult? ProcessingResult { get; set; }

    private bool ValidateOnly { get; set; } = true;
}
