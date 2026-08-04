namespace DfE.CheckPerformanceData.Application.Dashboard;

/// <summary>
/// Aggregates over submitted change requests (SubmittedUnCommitted + SubmittedCommitted) for
/// one checking window. Outcome counts are by the rules engine's DecisionStatus; requests the
/// engine has not decided yet count in TotalRequests but in none of the three outcome figures.
/// </summary>
public sealed class DashboardRequestAggregates
{
    public required int TotalRequests { get; init; }
    public required int AutoApproved { get; init; }
    public required int AutoRejected { get; init; }
    public required int RequiringScrutiny { get; init; }
    public required IReadOnlyList<long> SubmittingUrns { get; init; }
}

public interface IDashboardRequestRepository
{
    Task<DashboardRequestAggregates> GetRequestAggregatesAsync(
        Guid windowId, CancellationToken cancellationToken = default);
}
