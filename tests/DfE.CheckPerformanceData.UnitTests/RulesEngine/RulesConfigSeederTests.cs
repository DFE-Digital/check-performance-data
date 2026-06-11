using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Infrastructure.RulesEngine;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

/// <summary>
/// Behavioural tests for <see cref="RulesConfigSeeder"/> — the worker's startup self-seeder
/// that uploads the image-bundled rules-config JSON to storage when the blobs are absent,
/// and upgrades the stored <c>rules.json</c> when the bundled seed's version is strictly newer.
/// </summary>
public sealed class RulesConfigSeederTests : IDisposable
{
    private const string RulesBlobName = "rules.json";
    private const string LookupsBlobName = "country-languages.json";
    private const string RulesContent = """{ "version": "2026.06.11-01", "outcomes": [] }""";
    private const string LookupsContent = """{ "FR": ["French"] }""";

    private readonly string _seedDir;
    private readonly IRulesConfigStore _store = Substitute.For<IRulesConfigStore>();

    public RulesConfigSeederTests()
    {
        _seedDir = Path.Combine(Path.GetTempPath(), "cypd-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_seedDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_seedDir)) Directory.Delete(_seedDir, recursive: true);
    }

    private void WriteSeedFile(string name, string content) =>
        File.WriteAllText(Path.Combine(_seedDir, name), content);

    private void SetStored(RulesConfigType type, string content, string etag = "etag-1") =>
        _store.ReadAsync(type, Arg.Any<CancellationToken>())
            .Returns(new RulesConfigBlob(content, etag));

    private void SetMissing(RulesConfigType type) =>
        _store.ReadAsync(type, Arg.Any<CancellationToken>())
            .Throws(new RulesConfigNotFoundException("absent"));

    private static string RulesJsonWithVersion(string version) =>
        $$"""{ "version": "{{version}}", "outcomes": [] }""";

    private RulesConfigSeeder NewSeeder(bool seedOnStartup = true)
    {
        var options = Options.Create(new BlobRulesProviderOptions
        {
            RulesBlobName = RulesBlobName,
            LookupsBlobName = LookupsBlobName,
            SeedDirectory = _seedDir,
            SeedOnStartup = seedOnStartup
        });
        return new RulesConfigSeeder(_store, options, NullLogger<RulesConfigSeeder>.Instance);
    }

    // --- create-when-missing (unchanged semantics) ---

    [Fact]
    public async Task StartAsync_SeedsBothBlobs_FromBundledFiles_WhenMissing()
    {
        WriteSeedFile(RulesBlobName, RulesContent);
        WriteSeedFile(LookupsBlobName, LookupsContent);
        SetMissing(RulesConfigType.Rules);
        SetMissing(RulesConfigType.Lookups);

        await NewSeeder().StartAsync(CancellationToken.None);

        await _store.Received(1).WriteAsync(RulesConfigType.Rules, RulesContent, null, Arg.Any<CancellationToken>());
        await _store.Received(1).WriteAsync(RulesConfigType.Lookups, LookupsContent, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_SkipsWriteAndDoesNotThrow_WhenSeedFileMissing()
    {
        // Seed dir is empty (no files written) — nothing to upload.
        SetMissing(RulesConfigType.Rules);
        SetMissing(RulesConfigType.Lookups);

        await NewSeeder().StartAsync(CancellationToken.None);

        await _store.DidNotReceive().WriteAsync(
            Arg.Any<RulesConfigType>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_SwallowsConflict_FromConcurrentSeed()
    {
        WriteSeedFile(RulesBlobName, RulesContent);
        WriteSeedFile(LookupsBlobName, LookupsContent);
        SetMissing(RulesConfigType.Rules);
        SetMissing(RulesConfigType.Lookups);
        _store.WriteAsync(Arg.Any<RulesConfigType>(), Arg.Any<string>(), Arg.Is<string?>(e => e == null), Arg.Any<CancellationToken>())
            .Throws(new RulesConfigConflictException("seeded by another instance"));

        // Must not bubble out of StartAsync — that would crash the host.
        await NewSeeder().StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_SkipsSeed_WhenExistenceCheckFailsUnexpectedly()
    {
        WriteSeedFile(RulesBlobName, RulesContent);
        WriteSeedFile(LookupsBlobName, LookupsContent);
        _store.ReadAsync(Arg.Any<RulesConfigType>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("transient storage blip"));

        // Best-effort: a non-404 error must not block startup and must not risk an overwrite.
        await NewSeeder().StartAsync(CancellationToken.None);

        await _store.DidNotReceive().WriteAsync(
            Arg.Any<RulesConfigType>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_DoesNothing_WhenSeedingDisabled()
    {
        WriteSeedFile(RulesBlobName, RulesContent);

        await NewSeeder(seedOnStartup: false).StartAsync(CancellationToken.None);

        await _store.DidNotReceive().ReadAsync(Arg.Any<RulesConfigType>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceive().WriteAsync(
            Arg.Any<RulesConfigType>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    // --- version-gated rules upgrade ---

    [Fact]
    public async Task StartAsync_UpgradesRules_WhenBundledVersionNewer()
    {
        WriteSeedFile(RulesBlobName, RulesContent);
        SetStored(RulesConfigType.Rules, RulesJsonWithVersion("2026.05.14-01"), etag: "etag-1");
        SetStored(RulesConfigType.Lookups, LookupsContent);

        await NewSeeder().StartAsync(CancellationToken.None);

        // If-Match on the ETag just read — never an unconditional overwrite.
        await _store.Received(1).WriteAsync(RulesConfigType.Rules, RulesContent, "etag-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_DoesNotUpgradeRules_WhenVersionsEqual()
    {
        WriteSeedFile(RulesBlobName, RulesContent);
        SetStored(RulesConfigType.Rules, RulesJsonWithVersion("2026.06.11-01"));
        SetStored(RulesConfigType.Lookups, LookupsContent);

        // Idempotent restarts: same version must not rewrite the blob every startup.
        await NewSeeder().StartAsync(CancellationToken.None);

        await _store.DidNotReceive().WriteAsync(
            Arg.Any<RulesConfigType>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_DoesNotUpgradeRules_WhenStoredVersionNewer()
    {
        WriteSeedFile(RulesBlobName, RulesContent);
        SetStored(RulesConfigType.Rules, RulesJsonWithVersion("2026.06.12-094500"));
        SetStored(RulesConfigType.Lookups, LookupsContent);

        await NewSeeder().StartAsync(CancellationToken.None);

        await _store.DidNotReceive().WriteAsync(
            Arg.Any<RulesConfigType>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_DoesNotUpgradeRules_WhenStoredIsSameDayAdminSave()
    {
        // Seeds use yyyy.MM.dd-NN, admin saves yyyy.MM.dd-HHmmss. The suffixes must compare
        // numerically: ordinal string comparison would rank "-99" above "-134502".
        WriteSeedFile(RulesBlobName, RulesJsonWithVersion("2026.06.11-99"));
        SetStored(RulesConfigType.Rules, RulesJsonWithVersion("2026.06.11-134502"));
        SetStored(RulesConfigType.Lookups, LookupsContent);

        await NewSeeder().StartAsync(CancellationToken.None);

        await _store.DidNotReceive().WriteAsync(
            Arg.Any<RulesConfigType>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_UpgradesRules_WhenStoredVersionMissing()
    {
        // Admin saves always stamp a parseable version, so an unversioned blob is not an
        // admin artefact — restoring the known-good seed is the recovery path.
        WriteSeedFile(RulesBlobName, RulesContent);
        SetStored(RulesConfigType.Rules, """{ "outcomes": [] }""", etag: "etag-2");
        SetStored(RulesConfigType.Lookups, LookupsContent);

        await NewSeeder().StartAsync(CancellationToken.None);

        await _store.Received(1).WriteAsync(RulesConfigType.Rules, RulesContent, "etag-2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_UpgradesRules_WhenStoredJsonUnparseable()
    {
        WriteSeedFile(RulesBlobName, RulesContent);
        SetStored(RulesConfigType.Rules, "not json at all", etag: "etag-3");
        SetStored(RulesConfigType.Lookups, LookupsContent);

        await NewSeeder().StartAsync(CancellationToken.None);

        await _store.Received(1).WriteAsync(RulesConfigType.Rules, RulesContent, "etag-3", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_DoesNotUpgradeRules_WhenBundledVersionUnparseable()
    {
        // A malformed seed must never clobber a live blob.
        WriteSeedFile(RulesBlobName, RulesJsonWithVersion("v1.0"));
        SetStored(RulesConfigType.Rules, RulesJsonWithVersion("2026.05.14-01"));
        SetStored(RulesConfigType.Lookups, LookupsContent);

        await NewSeeder().StartAsync(CancellationToken.None);

        await _store.DidNotReceive().WriteAsync(
            Arg.Any<RulesConfigType>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_SwallowsConflict_WhenUpgradeWriteLosesEtagRace()
    {
        WriteSeedFile(RulesBlobName, RulesContent);
        SetStored(RulesConfigType.Rules, RulesJsonWithVersion("2026.05.14-01"), etag: "etag-1");
        SetStored(RulesConfigType.Lookups, LookupsContent);
        _store.WriteAsync(RulesConfigType.Rules, Arg.Any<string>(), "etag-1", Arg.Any<CancellationToken>())
            .Throws(new RulesConfigConflictException("admin saved between read and write"));

        // The concurrent writer's content is fresher; must not bubble or retry unconditionally.
        await NewSeeder().StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_NeverOverwritesLookups_WhenPresent()
    {
        // Even while the rules blob is being upgraded in the same run, country-languages.json
        // stays strictly seed-if-missing (it cannot carry a version field).
        WriteSeedFile(RulesBlobName, RulesContent);
        WriteSeedFile(LookupsBlobName, LookupsContent);
        SetStored(RulesConfigType.Rules, RulesJsonWithVersion("2026.05.14-01"), etag: "etag-1");
        SetStored(RulesConfigType.Lookups, """{ "DE": ["German"] }""");

        await NewSeeder().StartAsync(CancellationToken.None);

        await _store.Received(1).WriteAsync(RulesConfigType.Rules, RulesContent, "etag-1", Arg.Any<CancellationToken>());
        await _store.DidNotReceive().WriteAsync(
            RulesConfigType.Lookups, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
