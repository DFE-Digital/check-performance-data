using System.Text.Json;
using DfE.CheckPerformanceData.Web.Models.Dev;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// The automated-coverage panel reads a committed hat-coverage.json manifest that maps each
// automated HAT item to the test project and --filter that proves it. This pins the manifest to
// the model: every automated id the catalogue advertises must resolve to a well-formed entry, so
// the panel can never advertise an item it cannot map to a real test run.
public sealed class HatCoverageManifestTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static CoverageManifest ReadManifest()
    {
        var thisFile = ThisFilePath();
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
        var path = Path.Combine(
            repoRoot, "src", "DfE.CheckPerformanceData.Web", "wwwroot", "hat", "hat-coverage.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<CoverageManifest>(json, Options)!;
    }

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => path;

    [Fact]
    public void Manifest_IsWellFormedAndNonEmpty()
    {
        var manifest = ReadManifest();

        Assert.NotNull(manifest);
        Assert.NotNull(manifest.Items);
        Assert.NotEmpty(manifest.Items);
    }

    [Fact]
    public void EveryAutomatedCatalogueId_HasAManifestEntry()
    {
        var manifest = ReadManifest();
        var ids = manifest.Items.Select(i => i.Id).ToHashSet();

        foreach (var id in HatCatalog.AutomatedCoverageIds)
            Assert.Contains(id, ids);
    }

    [Fact]
    public void EveryManifestEntry_HasANonEmptyProjectAndFilter()
    {
        var manifest = ReadManifest();

        foreach (var item in manifest.Items)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Id), $"Entry has no id.");
            Assert.False(string.IsNullOrWhiteSpace(item.Project), $"Entry {item.Id} has no project.");
            Assert.False(string.IsNullOrWhiteSpace(item.Filter), $"Entry {item.Id} has no filter.");
            Assert.False(string.IsNullOrWhiteSpace(item.Description), $"Entry {item.Id} has no description.");
        }
    }

    [Fact]
    public void Manifest_MapsTheNamedAcceptanceTests()
    {
        var manifest = ReadManifest();
        var filters = string.Join("\n", manifest.Items.Select(i => i.Filter));

        Assert.Contains("EnqueueInsideRolledBackTransaction_LeavesNoMessage", filters);
        Assert.Contains("AzureQueueCutoverGuardTests", filters);
        Assert.Contains("Redelivery_DoesNotCreateDuplicateTicket", filters);
        Assert.Contains("AuditEntries_AreImmutable_UpdateAndDeleteRaise", filters);
    }

    private sealed record CoverageManifest(List<CoverageEntry> Items);

    private sealed record CoverageEntry(string Id, string Project, string Filter, string Description);
}
