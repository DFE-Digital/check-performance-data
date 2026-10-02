using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Locking;

namespace DfE.CheckPerformanceData.Persistence.ContentStaging;

// Postgres implementation of IContentStagingLock: a session-scoped advisory lock held on a
// connection of its own. Why its own connection — and why that is what makes the lock real — is
// written up on PostgresAdvisoryLock, which holds the mechanics.
public sealed class PostgresContentStagingLock(IPortalDbContext dbContext) : IContentStagingLock
{
    // int64 encoding of the ASCII bytes "CONTNTIM" — a stable, human-recognisable key.
    //
    // pg_locks splits a 64-bit advisory key across classid/objid as two int32s, so an
    // investigator will NOT see this constant there. The halves are spelled out so a query like
    //   select * from pg_locks where locktype = 'advisory' and classid = 1129270868;
    // finds it, and so the release is doable by hand if a pod dies holding the lock:
    //   select pg_terminate_backend(pid) from pg_locks
    //    where locktype = 'advisory' and classid = 1129270868 and objid = 1314820941;
    private const long LockKey = 0x434F_4E54_4E54_494DL;
    public const int LockKeyClassId = 0x434F_4E54;   // 1129270868
    public const int LockKeyObjId = 0x4E54_494D;     // 1314820941

    private readonly PostgresAdvisoryLock _lock = new(dbContext, LockKey);

    public Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default) =>
        _lock.TryAcquireAsync(cancellationToken);

    public Task ReleaseAsync(CancellationToken cancellationToken = default) =>
        _lock.ReleaseAsync(cancellationToken);
}
