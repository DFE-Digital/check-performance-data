using DfE.CheckPerformanceData.Application.WindowManagement.SampleData;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

/// <inheritdoc cref="ISampleSchoolRepository"/>
/// <remarks>The generic audit capture in <see cref="PortalDbContext"/> records each add and remove.</remarks>
public sealed class SampleSchoolRepository(PortalDbContext dbContext) : ISampleSchoolRepository
{
    private const string WindowLaestabIndex = "IX_CheckingWindowSampleSchools_CheckingWindowId_Laestab";

    public async Task<IReadOnlyList<SampleSchoolDto>> ListAsync(Guid windowId, CancellationToken cancellationToken) =>
        await dbContext.CheckingWindowSampleSchools
            .AsNoTracking()
            .Where(s => s.CheckingWindowId == windowId)
            .OrderBy(s => s.Laestab)
            .Select(s => new SampleSchoolDto(s.Id, s.CheckingWindowId, s.Laestab, s.Urn, s.AddedAt, s.AddedBy))
            .ToListAsync(cancellationToken);

    public Task<bool> WindowExistsAsync(Guid windowId, CancellationToken cancellationToken) =>
        dbContext.CheckingWindows.AnyAsync(w => w.Id == windowId, cancellationToken);

    public async Task<bool> AddAsync(NewSampleSchool school, CancellationToken cancellationToken)
    {
        var entity = new CheckingWindowSampleSchool
        {
            CheckingWindowId = school.WindowId,
            Laestab = school.Laestab,
            Urn = school.Urn,
            // The column is `timestamp without time zone`, and Npgsql refuses a Utc kind. The value
            // is still the UTC instant.
            AddedAt = DateTime.SpecifyKind(school.AddedAt, DateTimeKind.Unspecified),
            AddedBy = school.AddedBy
        };
        dbContext.CheckingWindowSampleSchools.Add(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: WindowLaestabIndex })
        {
            // Another admin added the same school first. Forget the failed row and its audit row.
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<bool> RemoveAsync(Guid windowId, Guid sampleSchoolId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.CheckingWindowSampleSchools
            .SingleOrDefaultAsync(s => s.Id == sampleSchoolId && s.CheckingWindowId == windowId, cancellationToken);
        if (entity is null)
            return false;

        // Tracked, so SaveChangesAsync writes the audit entry.
        dbContext.CheckingWindowSampleSchools.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
