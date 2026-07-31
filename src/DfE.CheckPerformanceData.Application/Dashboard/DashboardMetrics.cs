namespace DfE.CheckPerformanceData.Application.Dashboard;

public sealed class DashboardMetrics
{
    public required Guid WindowId { get; init; }
    public required string WindowTitle { get; init; }

    // School engagement
    public required int EligibleSchools { get; init; }
    public required int LoggedIn { get; init; }
    public required int NotLoggedIn { get; init; }
    public required int SchoolsSubmitted { get; init; }
    public required int LoggedInNotSubmitted { get; init; }

    // Amendment requests
    public required int TotalRequests { get; init; }
    public required int AutoApproved { get; init; }
    public required int AutoRejected { get; init; }
    public required int RequiringScrutiny { get; init; }

    /// <summary>When these figures were computed (cache fill time), UTC.</summary>
    public required DateTime RefreshedAtUtc { get; init; }
}
