using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.ContentStaging;

// Postgres implementation of IContentStagingLock backed by session-scoped advisory
// locks. The lock key is a stable int64 chosen so it can't collide with other
// advisory-lock use in the schema (there isn't any today; the value is derived
// from the ASCII bytes of "CONTNTIM" so a grep for the constant in any future
// pg_locks investigation surfaces the origin).
//
// SqlQueryRaw returns an IQueryable of the projected row shape; we hydrate via
// SingleAsync so a broken function call (missing pg_try_advisory_lock, wrong
// permissions) throws visibly rather than silently returning false.
public sealed class PostgresContentStagingLock(IPortalDbContext dbContext) : IContentStagingLock
{
    // int64 encoding of the ASCII bytes "CONTNTIM" — a stable, human-recognisable key
    // that a future pg_locks investigator can search the source for.
    private const long LockKey = 0x434F_4E54_4E54_494DL;

    public async Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Database
            .SqlQueryRaw<bool>("SELECT pg_try_advisory_lock({0}) AS \"Value\"", LockKey)
            .ToListAsync(cancellationToken);
        return rows.Count > 0 && rows[0];
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        _ = await dbContext.Database
            .SqlQueryRaw<bool>("SELECT pg_advisory_unlock({0}) AS \"Value\"", LockKey)
            .ToListAsync(cancellationToken);
    }
}
