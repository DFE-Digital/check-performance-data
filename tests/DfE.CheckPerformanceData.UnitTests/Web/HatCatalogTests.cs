using DfE.CheckPerformanceData.Web.Models.Dev;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// The encoded HAT inventory is the single source of truth for the guided runner, so it is pinned
// directly: the interactive list covers both phases, carries the specific showpiece items the
// UI-SPEC names with their expect text, every row has a stable id, and the automated-coverage ids
// the panel advertises stay in step with the manifest (asserted in HatCoverageManifestTests).
public sealed class HatCatalogTests
{
    [Fact]
    public void Interactive_CoversBothPhases()
    {
        Assert.Contains(HatCatalog.Interactive, i => i.Phase == HatPhase.Phase310);
        Assert.Contains(HatCatalog.Interactive, i => i.Phase == HatPhase.Phase313);
    }

    [Fact]
    public void Interactive_EveryItemHasAStableIdAndExpectText()
    {
        var ids = new HashSet<string>();
        foreach (var item in HatCatalog.Interactive)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Id));
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
            Assert.False(string.IsNullOrWhiteSpace(item.Expected));
            Assert.True(ids.Add(item.Id), $"Duplicate HAT item id: {item.Id}");
        }
    }

    [Theory]
    [InlineData("Failure-and-recovery demo")]
    [InlineData("Live animated board")]
    [InlineData("Track-my-request journey")]
    [InlineData("Real DLQ, not silent delete")]
    public void Interactive_IncludesNamedShowpieceItems(string title)
    {
        Assert.Contains(HatCatalog.Interactive, i => i.Title == title);
    }

    [Fact]
    public void Interactive_FailureDemoCarriesItsExpectText()
    {
        var item = HatCatalog.Interactive.Single(i => i.Title == "Failure-and-recovery demo");
        Assert.Contains("redrive it back to green", item.Expected);
    }

    [Fact]
    public void Interactive_BoardItemCarriesItsExpectText()
    {
        var item = HatCatalog.Interactive.Single(i => i.Title == "Live animated board");
        Assert.Contains("animates tokens left to right", item.Expected);
    }

    [Fact]
    public void Interactive_DriveAndFailureActionsPostToTheReusedDevEndpoints()
    {
        var posts = HatCatalog.Interactive
            .SelectMany(i => i.Actions)
            .Where(a => a.Method == HatActionMethod.Post)
            .Select(a => a.Url)
            .ToList();

        Assert.Contains("/dev/hat/drive?outcome=approved", posts);
        Assert.Contains("/dev/hat/inject-failure", posts);
        Assert.Contains("/dev/hat/seed-dlq", posts);
    }

    [Fact]
    public void AutomatedCoverageIds_AreNonEmptyAndUnique()
    {
        Assert.NotEmpty(HatCatalog.AutomatedCoverageIds);
        Assert.Equal(
            HatCatalog.AutomatedCoverageIds.Count,
            HatCatalog.AutomatedCoverageIds.Distinct().Count());
    }
}
