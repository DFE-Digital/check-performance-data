namespace DfE.CheckPerformanceData.Domain.Enums;

/// <summary>
/// Lifecycle of one egress run. Pulled → Preprocessing → (PreprocessingFailed | Preprocessed) →
/// Transferring → (TransferFailed | Transferred); Abandoned from any non-terminal state. Only
/// Pulled, Preprocessing, Preprocessed, Transferring and Transferred keep the run's outputs active
/// (see EgressRunOutput.IsActive) — a failed or abandoned run never blocks a new one.
/// </summary>
public enum EgressRunStatus
{
    Pulled,
    Preprocessing,
    PreprocessingFailed,
    Preprocessed,
    Transferring,
    TransferFailed,
    Transferred,
    Abandoned
}
