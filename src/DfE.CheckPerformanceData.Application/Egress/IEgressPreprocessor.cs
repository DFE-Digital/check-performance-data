using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Egress;

public sealed record EgressProgress(int Step, int TotalSteps, string StepName, string State, int RecordsIn, int RecordsOut, int FailureCount, bool IsComplete, bool IsError, string Message, EgressRunStatus? FinalStatus);

public interface IEgressPreprocessor
{
    /// <summary>Runs the pipeline for a Pulled (or PreprocessingFailed/Preprocessed — re-run) run. Yields one event per step state change; the last event has IsComplete = true.</summary>
    IAsyncEnumerable<EgressProgress> RunAsync(Guid runId, CancellationToken ct);
}
