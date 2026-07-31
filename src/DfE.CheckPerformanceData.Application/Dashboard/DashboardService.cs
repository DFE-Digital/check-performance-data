using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.WindowManagement;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Application.Dashboard;

public sealed class DashboardService(
    IPupilDataBlobClient pupilDataBlobClient,
    IOrganisationLoginRepository loginRepository,
    IDashboardRequestRepository requestRepository,
    IMemoryCache cache,
    IOptions<DashboardSettings> settings) : IDashboardService
{
    public async Task<DashboardMetrics> GetMetricsAsync(
        CheckingWindowDto window, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"dashboard-metrics:{window.Id}";
        if (cache.TryGetValue(cacheKey, out DashboardMetrics? cached) && cached is not null)
            return cached;

        var metrics = await BuildAsync(window, cancellationToken);
        cache.Set(cacheKey, metrics, TimeSpan.FromMinutes(settings.Value.RefreshMinutes));
        return metrics;
    }

    private async Task<DashboardMetrics> BuildAsync(
        CheckingWindowDto window, CancellationToken cancellationToken)
    {
        var eligible = (await pupilDataBlobClient.ListSchoolLaestabsAsync(window.Id))
            .ToHashSet(StringComparer.Ordinal);

        // Window dates are stored without a kind; Npgsql requires Utc for timestamptz params.
        var logins = await loginRepository.GetDistinctLoginsBetweenAsync(
            DateTime.SpecifyKind(window.StartDate, DateTimeKind.Utc),
            DateTime.SpecifyKind(window.EndDate, DateTimeKind.Utc),
            cancellationToken);

        // Only logins from eligible schools count as engagement — this also filters out
        // admin/LA organisations whose laestab does not match any pupil file.
        var eligibleLogins = logins.Where(l => eligible.Contains(l.NormalisedLaestab)).ToList();
        var loggedInSchools = eligibleLogins.Select(l => l.NormalisedLaestab).Distinct().Count();

        var aggregates = await requestRepository.GetRequestAggregatesAsync(window.Id, cancellationToken);
        var submittingUrns = aggregates.SubmittingUrns.ToHashSet();
        var loggedInNotSubmitted = eligibleLogins
            .Where(l => !submittingUrns.Contains(l.OrganisationUrn))
            .Select(l => l.NormalisedLaestab)
            .Distinct()
            .Count();

        return new DashboardMetrics
        {
            WindowId = window.Id,
            WindowTitle = window.Title,
            EligibleSchools = eligible.Count,
            LoggedIn = loggedInSchools,
            NotLoggedIn = eligible.Count - loggedInSchools,
            SchoolsSubmitted = submittingUrns.Count,
            LoggedInNotSubmitted = loggedInNotSubmitted,
            TotalRequests = aggregates.TotalRequests,
            AutoApproved = aggregates.AutoApproved,
            AutoRejected = aggregates.AutoRejected,
            RequiringScrutiny = aggregates.RequiringScrutiny,
            RefreshedAtUtc = DateTime.UtcNow,
        };
    }
}
