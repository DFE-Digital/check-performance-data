using DfE.CheckPerformanceData.Application.Dashboard;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class OrganisationLoginRepository(IPortalDbContext context) : IOrganisationLoginRepository
{
    public async Task RecordAsync(OrganisationLoginRecord record, CancellationToken cancellationToken = default)
    {
        context.OrganisationLogins.Add(new OrganisationLogin
        {
            UserId = record.UserId,
            OrganisationUrn = record.OrganisationUrn,
            Laestab = record.NormalisedLaestab,
            OrganisationName = record.OrganisationName,
            LoggedInAtUtc = DateTime.UtcNow,
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SchoolLogin>> GetDistinctLoginsBetweenAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
        => await context.OrganisationLogins
            .Where(l => l.LoggedInAtUtc >= fromUtc && l.LoggedInAtUtc <= toUtc)
            .Select(l => new SchoolLogin(l.OrganisationUrn, l.Laestab))
            .Distinct()
            .ToListAsync(cancellationToken);
}
