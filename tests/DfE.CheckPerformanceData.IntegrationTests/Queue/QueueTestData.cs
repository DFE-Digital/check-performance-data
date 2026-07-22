using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Queue;

// Test-local truncation for the queue and dead-letter tables. The tables are created by
// the queue migration; until that lands the truncation is a no-op so these tests fail on
// the service under test, not on setup.
internal static class QueueTestData
{
    private static readonly string[] CandidateTables =
    {
        "queue_messages",
        "queue_dead_letters",
        "QueueMessages",
        "QueueDeadLetters"
    };

    public static async Task ResetAsync(PostgresFixture fixture)
    {
        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        foreach (var table in CandidateTables)
        {
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"TRUNCATE TABLE ""{table}"" RESTART IDENTITY CASCADE;";
                await cmd.ExecuteNonQueryAsync();
            }
            catch (PostgresException)
            {
                // Table not present yet (migration not applied) — nothing to truncate.
            }
        }
    }
}
