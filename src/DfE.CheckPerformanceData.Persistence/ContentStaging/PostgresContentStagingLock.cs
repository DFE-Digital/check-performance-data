using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.Persistence.ContentStaging;

// Postgres implementation of IContentStagingLock backed by session-scoped advisory locks.
//
// The lock is taken on a connection of its own, opened here and held until release, rather than
// on the DbContext's. Advisory locks belong to the SESSION, so the lock only lasts as long as
// the particular physical connection that took it — and the import's connection is not stable.
// EF Core hands a pooled connection back after each command unless it is explicitly opened, and
// Npgsql resets a returned connection with DISCARD ALL, which runs pg_advisory_unlock_all().
// Worse, the context is registered with EnableRetryOnFailure, so a transient fault tears the
// connection down and retries the work on a fresh backend: the lock dies with the old one while
// the import carries on believing it is protected, and a second importer can then start.
//
// Both of those end the same way — a guard that reads as protection while excluding nobody,
// which is worse than having no guard at all, because everything downstream is written as
// though it holds. Owning a separate connection is what makes the guarantee real: nothing in
// EF's pooling or retry machinery can take it away.
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

    private NpgsqlConnection? _connection;

    public async Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
        {
            return true;
        }

        // Same database, separate session. Built from the context's own connection string so
        // there is one place configuring where this connects.
        var connection = new NpgsqlConnection(dbContext.Database.GetConnectionString());
        try
        {
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock(@key)";
            command.Parameters.AddWithValue("key", LockKey);
            var acquired = await command.ExecuteScalarAsync(cancellationToken) as bool? ?? false;

            if (!acquired)
            {
                await connection.DisposeAsync();
                return false;
            }

            _connection = connection;
            return true;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is null)
        {
            return;
        }

        var connection = _connection;
        _connection = null;
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_unlock(@key)";
            command.Parameters.AddWithValue("key", LockKey);
            await command.ExecuteScalarAsync(cancellationToken);
        }
        finally
        {
            // Closing the session drops the lock even if the unlock itself failed, so this is
            // the belt to the unlock's braces rather than merely tidying up.
            await connection.DisposeAsync();
        }
    }
}
