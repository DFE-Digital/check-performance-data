using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// The LDS egress account is "absent unless the environment supplies it": with no
// ConnectionStrings:EgressStorage the egress refuses to transfer with a clear message and the dev
// cleanup skips its blob sweep. A local-Azurite default in appsettings.json broke that contract on
// every deployed environment that had not been given an account — the app believed it had one at
// 127.0.0.1:10000 inside the pod, every transfer and every cleanup failed after the SDK's retries,
// and the review app for PR #441 was left with egress rows its start-up seeder could not delete.
// Local runs get the string from docker-compose.yaml and the launch profiles, never from here.
public sealed class AppSettingsEgressStorageTests
{
    [Fact]
    public void The_egress_storage_connection_string_has_no_default_in_appsettings()
    {
        var json = ReadAppSettings("appsettings.json");
        Assert.True(json.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings));

        Assert.False(connectionStrings.TryGetProperty("EgressStorage", out _),
            "appsettings.json must not ship a ConnectionStrings:EgressStorage default — an environment without an " +
            "egress account must read as 'not configured', not as a dead local Azurite endpoint.");
    }

    private static JsonDocument ReadAppSettings(string fileName, [CallerFilePath] string testFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(testFile)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Makefile")))
        {
            dir = dir.Parent;
        }
        if (dir is null) throw new InvalidOperationException("Could not locate repository root.");

        var path = Path.Combine(dir.FullName, "src", "DfE.CheckPerformanceData.Web", fileName);
        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
