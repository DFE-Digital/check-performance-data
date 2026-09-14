using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// The one place that maps an EgressRunStatus to what a user reads — the saved-runs "Stage"
/// column (Index.cshtml) and any refusal copy that would otherwise leak the C# enum member name
/// (e.g. "PreprocessingFailed" rendering literally instead of "Preprocessing failed").
/// </summary>
public static class EgressRunStatuses
{
    public static string Label(EgressRunStatus status) => status switch
    {
        EgressRunStatus.Pulled => "Data pulled",
        EgressRunStatus.Preprocessing => "Preprocessing",
        EgressRunStatus.PreprocessingFailed => "Preprocessing failed",
        EgressRunStatus.Preprocessed => "Ready to transfer",
        EgressRunStatus.Transferring => "Transferring",
        EgressRunStatus.TransferFailed => "Transfer failed",
        _ => status.ToString()
    };
}
