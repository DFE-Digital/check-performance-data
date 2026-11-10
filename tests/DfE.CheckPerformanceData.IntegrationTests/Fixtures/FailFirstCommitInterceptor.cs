using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Fixtures;

/// <summary>
/// Forces one retry after the writes are done. The first commit throws a 40001, so the
/// transaction rolls back although every SaveChangesAsync in it succeeded. The change tracker
/// then holds values that the database does not have. Every later commit runs.
/// </summary>
public sealed class FailFirstCommitInterceptor : DbTransactionInterceptor
{
    private int _commits;

    /// <summary>How many commits EF tried, including the one that failed.</summary>
    public int Commits => _commits;

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _commits) == 1)
        {
            throw new PostgresException(
                "forced serialization failure at commit (test)", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure);
        }

        return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
    }
}
