using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Fixtures;

/// <summary>
/// Forces one retry of the execution strategy. The first command whose text contains
/// <paramref name="fragment"/> throws a 40001 (serialization failure). Npgsql classifies it as
/// transient, so EnableRetryOnFailure runs the whole delegate again. Every later command runs.
/// Deterministic: no timing, no second connection.
/// </summary>
public sealed class FailFirstCommandInterceptor(string fragment) : DbCommandInterceptor
{
    private int _matches;

    /// <summary>How many matching commands EF tried, including the one that failed.</summary>
    public int Matches => _matches;

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        ThrowOnFirstMatch(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ThrowOnFirstMatch(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void ThrowOnFirstMatch(DbCommand command)
    {
        if (!command.CommandText.Contains(fragment, StringComparison.Ordinal))
            return;

        if (Interlocked.Increment(ref _matches) == 1)
        {
            throw new PostgresException(
                "forced serialization failure (test)", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure);
        }
    }
}
