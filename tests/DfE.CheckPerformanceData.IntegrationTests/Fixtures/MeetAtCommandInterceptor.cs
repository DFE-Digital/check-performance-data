using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DfE.CheckPerformanceData.IntegrationTests.Fixtures;

/// <summary>
/// Makes a race certain. Share one instance between <paramref name="parties"/> contexts. Each
/// context waits at its first command that contains <paramref name="fragment"/> until all of them
/// are there, then all continue. So every context has done its read before any one writes.
/// The timeout stops a deadlock if the code under test stops sending the command.
/// </summary>
public sealed class MeetAtCommandInterceptor(string fragment, int parties, TimeSpan timeout) : DbCommandInterceptor
{
    private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrived;

    /// <summary>How many commands waited at the meeting point.</summary>
    public int Arrived => _arrived;

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains(fragment, StringComparison.Ordinal))
        {
            var arrived = Interlocked.Increment(ref _arrived);
            if (arrived == parties)
                _allArrived.TrySetResult();
            if (arrived <= parties)
                await _allArrived.Task.WaitAsync(timeout, cancellationToken);
        }

        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
