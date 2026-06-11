using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Infrastructure.RulesEngine;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

/// <summary>
/// Behavioural tests for <see cref="BlobRulesProvider"/>. The provider's
/// <see cref="BlobRulesProvider.RefreshAsync"/> is public so each scenario
/// drives it directly without waiting on <see cref="PeriodicTimer"/>.
/// </summary>
public sealed class BlobRulesProviderTests
{
    private const string RulesBlobName = "rules.json";
    private const string LookupsBlobName = "country-languages.json";

    private const string ValidRulesJson = """
        {
          "version": "v1.0",
          "updatedAt": "2026-05-14T10:00:00Z",
          "outcomes": [
            {
              "key": "Deceased",
              "label": "Deceased",
              "rules": [
                { "id": "DEC-1", "status": "AutoApproved", "when": "otherwise" }
              ]
            }
          ]
        }
        """;

    private const string ValidRulesJsonV2 = """
        {
          "version": "v2.0",
          "updatedAt": "2026-05-15T10:00:00Z",
          "outcomes": [
            {
              "key": "Deceased",
              "label": "Deceased",
              "rules": [
                { "id": "DEC-1", "status": "Scrutiny", "when": "otherwise" }
              ]
            }
          ]
        }
        """;

    private const string ValidLookupsJson = """
        { "AU": ["English"], "FR": ["French"] }
        """;

    private const string InvalidRulesJson_MissingOtherwise = """
        {
          "version": "v1.0",
          "updatedAt": "2026-05-14T10:00:00Z",
          "outcomes": [
            { "key": "Deceased", "label": "Deceased",
              "rules": [ { "id": "DEC-1", "status": "AutoApproved",
                           "when": { "field": "keyStage", "eq": "KS2" } } ] }
          ]
        }
        """;

    [Fact]
    public async Task Construction_InstallsColdFallback()
    {
        // Before any refresh, Current must be a safe snapshot — never null.
        var (sut, _, _) = Build();

        Assert.NotNull(sut.Current);
        Assert.Equal(RulesHealth.ColdFallback, sut.Current.Health);
        Assert.Equal("cold-fallback", sut.Current.Version);
    }

    [Fact]
    public async Task RefreshAsync_OnFirstSuccess_InstallsHealthySnapshot()
    {
        var (sut, reader, _) = Build();
        reader.FetchAsync(RulesBlobName, null, Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidRulesJson, "etag-rules-1"));
        reader.FetchAsync(LookupsBlobName, null, Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidLookupsJson, "etag-lookups-1"));

        await sut.RefreshAsync(CancellationToken.None);

        Assert.Equal(RulesHealth.Healthy, sut.Current.Health);
        Assert.Equal("v1.0", sut.Current.Version);
        Assert.Single(sut.Current.Rules.Outcomes);
        Assert.True(sut.Current.Lookups.CountryHasOfficialLanguage("AU", "English"));
    }

    [Fact]
    public async Task RefreshAsync_LookupsWithUnderscoreCommentKeys_ParsesAndSkipsThem()
    {
        // The seed country-languages.json carries a "_comment" string entry by
        // convention; the provider must skip underscore-prefixed keys, not reject
        // the whole document.
        var (sut, reader, _) = Build();
        reader.FetchAsync(RulesBlobName, null, Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidRulesJson, "etag-rules-1"));
        reader.FetchAsync(LookupsBlobName, null, Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(
                  """{ "_comment": "ISO code -> official languages", "AU": ["English"] }""",
                  "etag-lookups-1"));

        await sut.RefreshAsync(CancellationToken.None);

        Assert.Equal(RulesHealth.Healthy, sut.Current.Health);
        Assert.True(sut.Current.Lookups.CountryHasOfficialLanguage("AU", "English"));
        Assert.False(sut.Current.Lookups.CountryHasOfficialLanguage("_comment", "English"));
    }

    [Fact]
    public async Task RefreshAsync_BothErrors_KeepsColdFallback_DoesNotUpgradeHealth()
    {
        var (sut, reader, _) = Build();
        reader.FetchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Error("404 Not Found"));

        await sut.RefreshAsync(CancellationToken.None);

        Assert.Equal(RulesHealth.ColdFallback, sut.Current.Health);
        Assert.Equal("cold-fallback", sut.Current.Version);
    }

    [Fact]
    public async Task RefreshAsync_SendsLastETag_OnSecondRefresh()
    {
        var (sut, reader, _) = Build();
        reader.FetchAsync(RulesBlobName, null, Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidRulesJson, "etag-rules-1"));
        reader.FetchAsync(LookupsBlobName, null, Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidLookupsJson, "etag-lookups-1"));

        await sut.RefreshAsync(CancellationToken.None);

        // Second refresh should send the last good ETags. Use the same mock for both calls
        // and just check the args on the second invocation.
        reader.FetchAsync(RulesBlobName, "etag-rules-1", Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.NotModified("etag-rules-1"));
        reader.FetchAsync(LookupsBlobName, "etag-lookups-1", Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.NotModified("etag-lookups-1"));

        await sut.RefreshAsync(CancellationToken.None);

        await reader.Received().FetchAsync(RulesBlobName, "etag-rules-1", Arg.Any<CancellationToken>());
        await reader.Received().FetchAsync(LookupsBlobName, "etag-lookups-1", Arg.Any<CancellationToken>());
        // Snapshot didn't change.
        Assert.Equal("v1.0", sut.Current.Version);
    }

    [Fact]
    public async Task RefreshAsync_NotModifiedOnBothBlobs_KeepsSnapshot()
    {
        var (sut, reader, _) = Build();
        reader.FetchAsync(RulesBlobName, null, Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidRulesJson, "etag-rules-1"));
        reader.FetchAsync(LookupsBlobName, null, Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidLookupsJson, "etag-lookups-1"));
        await sut.RefreshAsync(CancellationToken.None);

        // Subsequent refresh: both unmodified.
        reader.ClearReceivedCalls();
        reader.FetchAsync(RulesBlobName, Arg.Any<string?>(), Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.NotModified("etag-rules-1"));
        reader.FetchAsync(LookupsBlobName, Arg.Any<string?>(), Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.NotModified("etag-lookups-1"));

        var versionBefore = sut.Current.Version;
        var loadedAtBefore = sut.Current.LoadedAt;

        await sut.RefreshAsync(CancellationToken.None);

        Assert.Equal(versionBefore, sut.Current.Version);
        Assert.Equal(loadedAtBefore, sut.Current.LoadedAt);
    }

    [Fact]
    public async Task RefreshAsync_NewVersion_ReplacesSnapshotAtomically()
    {
        var (sut, reader, _) = Build();
        reader.FetchAsync(RulesBlobName, null, Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidRulesJson, "etag-rules-1"));
        reader.FetchAsync(LookupsBlobName, null, Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidLookupsJson, "etag-lookups-1"));
        await sut.RefreshAsync(CancellationToken.None);

        // New rules version arrives.
        reader.FetchAsync(RulesBlobName, "etag-rules-1", Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidRulesJsonV2, "etag-rules-2"));
        reader.FetchAsync(LookupsBlobName, "etag-lookups-1", Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.NotModified("etag-lookups-1"));

        await sut.RefreshAsync(CancellationToken.None);

        Assert.Equal("v2.0", sut.Current.Version);
        Assert.Equal(DecisionStatus.Scrutiny, sut.Current.Rules.Outcomes[0].Rules[0].Status);
        // Lookups carried forward unchanged.
        Assert.True(sut.Current.Lookups.CountryHasOfficialLanguage("AU", "English"));
    }

    [Fact]
    public async Task RefreshAsync_ValidationFailure_KeepsPreviousSnapshot()
    {
        var (sut, reader, _) = Build();
        // Seed with a good snapshot first.
        reader.FetchAsync(RulesBlobName, null, Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidRulesJson, "etag-rules-1"));
        reader.FetchAsync(LookupsBlobName, null, Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidLookupsJson, "etag-lookups-1"));
        await sut.RefreshAsync(CancellationToken.None);

        // Now serve an invalid ruleset (no terminal otherwise).
        reader.FetchAsync(RulesBlobName, "etag-rules-1", Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(InvalidRulesJson_MissingOtherwise, "etag-rules-bad"));
        reader.FetchAsync(LookupsBlobName, "etag-lookups-1", Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.NotModified("etag-lookups-1"));

        await sut.RefreshAsync(CancellationToken.None);

        // Snapshot did not change to the invalid version.
        Assert.Equal("v1.0", sut.Current.Version);
        Assert.Equal(RulesHealth.Healthy, sut.Current.Health);
    }

    [Fact]
    public async Task RefreshAsync_MalformedJson_KeepsPreviousSnapshot()
    {
        var (sut, reader, _) = Build();
        reader.FetchAsync(RulesBlobName, null, Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidRulesJson, "etag-rules-1"));
        reader.FetchAsync(LookupsBlobName, null, Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidLookupsJson, "etag-lookups-1"));
        await sut.RefreshAsync(CancellationToken.None);

        reader.FetchAsync(RulesBlobName, "etag-rules-1", Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success("{ this is not json", "etag-rules-bad"));
        reader.FetchAsync(LookupsBlobName, "etag-lookups-1", Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.NotModified("etag-lookups-1"));

        await sut.RefreshAsync(CancellationToken.None);

        Assert.Equal("v1.0", sut.Current.Version);
    }

    [Fact]
    public async Task RefreshAsync_FailureBeforeStalenessThreshold_StaysHealthy()
    {
        var (sut, reader, fakeTime) = Build();
        reader.FetchAsync(RulesBlobName, null, Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidRulesJson, "etag-rules-1"));
        reader.FetchAsync(LookupsBlobName, null, Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidLookupsJson, "etag-lookups-1"));
        await sut.RefreshAsync(CancellationToken.None);
        Assert.Equal(RulesHealth.Healthy, sut.Current.Health);

        // Now refresh starts failing, but only briefly.
        reader.FetchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Error("transient"));
        fakeTime.Advance(TimeSpan.FromSeconds(60)); // under 1800s threshold

        await sut.RefreshAsync(CancellationToken.None);

        Assert.Equal(RulesHealth.Healthy, sut.Current.Health);
    }

    [Fact]
    public async Task RefreshAsync_FailurePastStalenessThreshold_MarksStale()
    {
        var (sut, reader, fakeTime) = Build();
        reader.FetchAsync(RulesBlobName, null, Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidRulesJson, "etag-rules-1"));
        reader.FetchAsync(LookupsBlobName, null, Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidLookupsJson, "etag-lookups-1"));
        await sut.RefreshAsync(CancellationToken.None);

        reader.FetchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Error("still failing"));
        fakeTime.Advance(TimeSpan.FromSeconds(1801)); // just past 1800s default

        await sut.RefreshAsync(CancellationToken.None);

        Assert.Equal(RulesHealth.StaleLastKnownGood, sut.Current.Health);
        // Version unchanged.
        Assert.Equal("v1.0", sut.Current.Version);
    }

    [Fact]
    public async Task RefreshAsync_AfterRecovery_HealthReturnsToHealthy()
    {
        var (sut, reader, fakeTime) = Build();
        reader.FetchAsync(RulesBlobName, null, Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidRulesJson, "etag-rules-1"));
        reader.FetchAsync(LookupsBlobName, null, Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidLookupsJson, "etag-lookups-1"));
        await sut.RefreshAsync(CancellationToken.None);

        // Fail past staleness.
        reader.FetchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Error("down"));
        fakeTime.Advance(TimeSpan.FromSeconds(2000));
        await sut.RefreshAsync(CancellationToken.None);
        Assert.Equal(RulesHealth.StaleLastKnownGood, sut.Current.Health);

        // Recovery: blob comes back with a new version.
        reader.FetchAsync(RulesBlobName, "etag-rules-1", Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidRulesJsonV2, "etag-rules-2"));
        reader.FetchAsync(LookupsBlobName, "etag-lookups-1", Arg.Any<CancellationToken>())
              .Returns(RulesBlobFetch.Success(ValidLookupsJson, "etag-lookups-1"));

        await sut.RefreshAsync(CancellationToken.None);

        Assert.Equal(RulesHealth.Healthy, sut.Current.Health);
        Assert.Equal("v2.0", sut.Current.Version);
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        // The provider is registered both as a singleton and as an IHostedService
        // factory returning the same instance, so the container disposes it twice
        // on shutdown — DisposeAsync must be idempotent.
        var (sut, _, _) = Build();
        await sut.StartAsync(CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);

        await sut.DisposeAsync();
        await sut.DisposeAsync();
    }

    // --- helpers ---

    private static (BlobRulesProvider sut, IRulesBlobReader reader, FakeTimeProvider time) Build()
    {
        var reader = Substitute.For<IRulesBlobReader>();
        var options = Options.Create(new BlobRulesProviderOptions
        {
            RulesBlobName = RulesBlobName,
            LookupsBlobName = LookupsBlobName,
            RefreshIntervalSeconds = 300,
            StalenessWarningSeconds = 1800,
        });
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 5, 14, 10, 0, 0, TimeSpan.Zero));
        var sut = new BlobRulesProvider(reader, new RuleSetValidator(), options, time, NullLogger<BlobRulesProvider>.Instance);
        return (sut, reader, time);
    }

    /// <summary>
    /// Minimal TimeProvider for tests. Manually advanced; never auto-ticks.
    /// Avoids pulling in Microsoft.Extensions.TimeProvider.Testing just for these tests.
    /// </summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset now) { _now = now; }
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
