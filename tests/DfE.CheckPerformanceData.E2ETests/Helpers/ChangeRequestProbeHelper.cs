using Npgsql;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

// The "Cancelling from the summary discards the enquiry" AC across the enquiry suites proves
// nothing lands in ChangeRequests by probing the table directly. That probe only works where the
// test process can reach the same Postgres the web app writes to — the local docker-compose stack,
// wired up via CPD_E2E_DB in docker-compose.e2e.yml. The GitHub Actions `e2e` job runs dotnet test
// against a deployed AKS review app: it provides no local Postgres and does not set CPD_E2E_DB, so
// the old localhost:5432 default could never connect and blew up with "Connection refused" (see
// AB#298304, the missing-qualification cancel test, and its result-does-not-belong sibling).
//
// The probe is therefore opt-in: it returns null (meaning "cannot observe the ChangeRequests
// table") unless CPD_E2E_DB is explicitly provided. Callers treat null as "probe unavailable" and
// skip the row-count assertion gracefully instead of failing the build on an unreachable DB.
public static class ChangeRequestProbeHelper
{
    // Returns the ChangeRequests row count, or null when CPD_E2E_DB is not set (the probe is only
    // meaningful where the caller can reach the app's Postgres, i.e. the local compose stack).
    public static async Task<long?> CountChangeRequestsAsync()
    {
        var cs = Environment.GetEnvironmentVariable("CPD_E2E_DB");
        if (string.IsNullOrWhiteSpace(cs))
        {
            return null;
        }

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"ChangeRequests\"", conn);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}