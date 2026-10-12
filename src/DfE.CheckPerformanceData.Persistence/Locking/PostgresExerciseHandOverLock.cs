using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Persistence.Contexts;

namespace DfE.CheckPerformanceData.Persistence.Locking;

// AB#302158: Postgres implementation of IExerciseHandOverLock. See PostgresAdvisoryLock for why
// the lock lives on a connection of its own.
public sealed class PostgresExerciseHandOverLock(IPortalDbContext dbContext) : IExerciseHandOverLock
{
    // int64 encoding of the ASCII bytes "EXHANDOV". As with the content-staging lock, pg_locks
    // shows the two int32 halves, not this constant:
    //   select * from pg_locks where locktype = 'advisory' and classid = 1163413569;
    // and to release it by hand if a pod dies holding it (a dead pod's session normally drops
    // it by itself):
    //   select pg_terminate_backend(pid) from pg_locks
    //    where locktype = 'advisory' and classid = 1163413569 and objid = 1313099606;
    private const long LockKey = 0x4558_4841_4E44_4F56L;
    public const int LockKeyClassId = 0x4558_4841;   // 1163413569
    public const int LockKeyObjId = 0x4E44_4F56;     // 1313099606

    private readonly PostgresAdvisoryLock _lock = new(dbContext, LockKey);

    public Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default) =>
        _lock.TryAcquireAsync(cancellationToken);

    public Task ReleaseAsync(CancellationToken cancellationToken = default) =>
        _lock.ReleaseAsync(cancellationToken);
}
