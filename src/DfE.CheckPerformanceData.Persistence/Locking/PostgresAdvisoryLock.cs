using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.Persistence.Locking;

// A Postgres session-scoped advisory lock on one 64-bit key. Shared by the content-staging import
// lock and the exercise hand-over lock, which differ only in their key.
//
// The lock is taken on a connection of its own, opened here and held until release, rather than
// on the DbContext's. Advisory locks belong to the SESSION, so the lock only lasts as long as
// the particular physical connection that took it — and a DbContext's connection is not stable.
// EF Core hands a pooled connection back after each command unless it is explicitly opened, and
// Npgsql resets a returned connection with DISCARD ALL, which runs pg_advisory_unlock_all().
// Worse, the context is registered with EnableRetryOnFailure, so a transient fault tears the
// connection down and retries the work on a fresh backend: the lock dies with the old one while
// the work carries on believing it is protected, and a second holder can then start.
//
// Both of those end the same way — a guard that reads as protection while excluding nobody,
// which is worse than having no guard at all, because everything downstream is written as
// though it holds. Owning a separate connection is what makes the guarantee real: nothing in
// EF's pooling or retry machinery can take it away.
internal sealed class PostgresAdvisoryLock(IPortalDbContext dbContext, long key)
{
    private NpgsqlConnection? _connection;

    public async Task<bool> TryAcquireAsync(CancellationToken cancellationToken)
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
            command.Parameters.AddWithValue("key", key);
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

    public async Task ReleaseAsync(CancellationToken cancellationToken)
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
            command.Parameters.AddWithValue("key", key);
            await command.ExecuteScalarAsync(cancellationToken);
        }
        finally
        {
            // Disposing hands the connection back to Npgsql's pool; it does not necessarily end
            // the session. If the unlock above failed on a connection that is still healthy, the
            // lock therefore lingers until the pool resets that connection on its next use
            // (DISCARD ALL runs pg_advisory_unlock_all) or prunes it as idle — minutes, not for
            // good. A broken connection is closed outright, which drops the lock at once.
            await connection.DisposeAsync();
        }
    }
}
