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
// The connection is held open for as long as the lock is: pg_try_advisory_lock is scoped to
// the SESSION, and EF Core hands the connection back to the pool after each command unless it
// has been opened explicitly. Without that, the lock is taken and dropped inside
// TryAcquireAsync — every caller is granted it, pg_locks shows nothing, and pg_advisory_unlock
// returns false because there was never anything to unlock. The guard reads as protection
// while excluding nobody, which is worse than having no guard at all.
public sealed class PostgresContentStagingLock(IPortalDbContext dbContext) : IContentStagingLock
{
    // int64 encoding of the ASCII bytes "CONTNTIM" — a stable, human-recognisable key
    // that a future pg_locks investigator can search the source for.
    private const long LockKey = 0x434F_4E54_4E54_494DL;

    private bool _held;

    public async Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        if (_held)
        {
            return true;
        }

        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var rows = await dbContext.Database
                .SqlQueryRaw<bool>("SELECT pg_try_advisory_lock({0}) AS \"Value\"", LockKey)
                .ToListAsync(cancellationToken);
            _held = rows.Count > 0 && rows[0];
        }
        catch
        {
            // Give the connection back rather than stranding it on a failed acquire.
            await dbContext.Database.CloseConnectionAsync();
            throw;
        }

        if (!_held)
        {
            await dbContext.Database.CloseConnectionAsync();
        }

        return _held;
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        // Releasing something never acquired would unlock a peer's lock if the connection
        // happened to be shared, and otherwise just wastes a round trip.
        if (!_held)
        {
            return;
        }

        try
        {
            _ = await dbContext.Database
                .SqlQueryRaw<bool>("SELECT pg_advisory_unlock({0}) AS \"Value\"", LockKey)
                .ToListAsync(cancellationToken);
        }
        finally
        {
            _held = false;
            await dbContext.Database.CloseConnectionAsync();
        }
    }
}
