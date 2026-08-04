namespace DfE.CheckPerformanceData.Application.Dashboard;

/// <summary>A login row to append. Laestab must already be normalised (digits only).</summary>
public sealed record OrganisationLoginRecord(
    long OrganisationUrn,
    string NormalisedLaestab,
    string OrganisationName);

/// <summary>One distinct school seen logging in during a period.</summary>
public sealed record SchoolLogin(long OrganisationUrn, string NormalisedLaestab);

public interface IOrganisationLoginRepository
{
    Task RecordAsync(OrganisationLoginRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Distinct (URN, laestab) pairs with at least one login in [fromUtc, toUtc] inclusive.
    /// </summary>
    Task<IReadOnlyList<SchoolLogin>> GetDistinctLoginsBetweenAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}
