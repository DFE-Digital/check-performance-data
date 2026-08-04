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
        cache.Set(cacheKey, metrics, TimeSpan.FromMinutes(settings.Value.EffectiveRefreshMinutes));
        return metrics;
    }

    private async Task<DashboardMetrics> BuildAsync(
        CheckingWindowDto window, CancellationToken cancellationToken)
    {
        var eligible = (await pupilDataBlobClient.ListSchoolLaestabsAsync(window.Id, cancellationToken))
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
        // Submissions arrive keyed by URN but every other school tile counts eligible
        // schools by laestab, so map URN → laestab through the window's own logins (a
        // submitter must have signed in during the window). Keeps all five engagement
        // tiles on one population and one key: a submitting org with no eligible login
        // (LA, admin, missing pupil blob) no longer inflates "Submitted amendments",
        // and a school seen under two URNs cannot count as "logged in, not submitted".
        var submittedLaestabs = eligibleLogins
            .Where(l => submittingUrns.Contains(l.OrganisationUrn))
            .Select(l => l.NormalisedLaestab)
            .ToHashSet(StringComparer.Ordinal);

        return new DashboardMetrics
        {
            WindowId = window.Id,
            WindowTitle = window.Title,
            EligibleSchools = eligible.Count,
            LoggedIn = loggedInSchools,
            NotLoggedIn = eligible.Count - loggedInSchools,
            SchoolsSubmitted = submittedLaestabs.Count,
            LoggedInNotSubmitted = loggedInSchools - submittedLaestabs.Count,
            TotalRequests = aggregates.TotalRequests,
            AutoApproved = aggregates.AutoApproved,
            AutoRejected = aggregates.AutoRejected,
            RequiringScrutiny = aggregates.RequiringScrutiny,
            RefreshedAtUtc = DateTime.UtcNow,
        };
    }
}
