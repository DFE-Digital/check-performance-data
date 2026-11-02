using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Fixtures;

/// <summary>
/// Forces one retry of the execution strategy. The first command that inserts into
/// "ChangeRequests" throws a 40001 (serialization failure), which Npgsql classifies as
/// transient, so EnableRetryOnFailure re-runs the whole delegate. Every later insert runs.
/// Deterministic: no timing, no second connection.
/// </summary>
public sealed class FailFirstChangeRequestInsertInterceptor : DbCommandInterceptor
{
    private int _changeRequestInserts;

    /// <summary>How many "ChangeRequests" insert commands EF tried, including the one that failed.</summary>
    public int ChangeRequestInserts => _changeRequestInserts;

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        ThrowOnFirstChangeRequestInsert(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ThrowOnFirstChangeRequestInsert(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void ThrowOnFirstChangeRequestInsert(DbCommand command)
    {
        if (!command.CommandText.Contains("INSERT INTO \"ChangeRequests\"", StringComparison.Ordinal))
            return;

        if (Interlocked.Increment(ref _changeRequestInserts) == 1)
        {
            // Same SqlState Postgres sends when it cancels a serializable transaction.
            throw new PostgresException(
                "forced serialization failure (test)", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure);
        }
    }
}
