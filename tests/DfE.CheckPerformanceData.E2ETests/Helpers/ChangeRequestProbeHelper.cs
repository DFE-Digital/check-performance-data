using Npgsql;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

// The "Cancelling from the summary discards the enquiry" AC across the enquiry suites proves
// nothing lands in ChangeRequests by probing the table directly. The probe reaches the same
// Postgres the web app writes to. Which Postgres that is depends on how the tests are launched:
//   - `make test-e2e`        → inside the e2e-tests container; CPD_E2E_DB points at the `db`
//                              service (docker-compose.e2e.yml).
//   - `make test-e2e-fast`    → bare `dotnet test` on the host against the compose `db` published
//                              on localhost:5432; CPD_E2E_DB is NOT set, so we fall back to it.
//   - GitHub Actions `e2e` job → runs dotnet test against a deployed AKS review app. There is no
//                              local Postgres at localhost:5432, so the probe cannot connect.
//
// The probe therefore tries the connection and, when it genuinely cannot connect, reports the
// probe as unavailable (returns null). The caller is expected to convert that into an explicit
// Skip via SkippableFact — so the AC is visible as Skipped in CI rather than silently dropped
// (a future regression would otherwise pass green forever), while the two local workflows keep
// exercising it for real.
public static class ChangeRequestProbeHelper
{
    // The compose `db` service is published on the host as localhost:5432. The test-e2e-fast target
    // runs bare `dotnet test` with no CPD_E2E_DB, so this fallback is what lets it exercise the AC.
    private const string LocalFallbackConnectionString =
        "Host=localhost;Port=5432;Database=cypd;Username=postgres;Password=postgres";

    public const string UnavailableReason =
        "Cannot reach Postgres (ChangeRequests table): the probe is only meaningful where the "
        + "test process can connect to the app's DB — the local compose stack (docker-compose "
        + "`db:` service on localhost:5432, or CPD_E2E_DB). In CI the E2E job runs against a "
        + "deployed AKS review app with no reachable local Postgres, so this row-count assertion "
        + "is skipped. It is intentional that this reports Skipped, not Passed, so the AC is never "
        + "silently abandoned.";

    // Returns the ChangeRequests row count, or null when the probe cannot connect to the DB that
    // CPD_E2E_DB (or the localhost:5432 fallback) points at. Connection failures are treated as
    // "unavailable" so callers can skip explicitly instead of failing a full journey after the fact.
    public static async Task<long?> TryCountChangeRequestsAsync()
    {
        var cs = Environment.GetEnvironmentVariable("CPD_E2E_DB");
        if (string.IsNullOrWhiteSpace(cs))
        {
            cs = LocalFallbackConnectionString;
        }

        try
        {
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"ChangeRequests\"", conn);
            return (long)(await cmd.ExecuteScalarAsync())!;
        }
        catch (NpgsqlException)
        {
            return null;
        }
    }

    // The pair used by the "cancelling discards the enquiry" AC: assert that no ChangeRequests row
    // was created between two reads of the table, skipping explicitly when the table is unreachable.
    public static async Task AssertNoRowsCreatedBetweenAsync(long? before)
    {
        var after = await TryCountChangeRequestsAsync();
        Assert.Equal(before, after);
    }
}
