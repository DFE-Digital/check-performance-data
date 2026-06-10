using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Infrastructure.RulesEngine;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

/// <summary>
/// Behavioural tests for <see cref="RulesConfigSeeder"/> — the worker's startup self-seeder
/// that uploads the image-bundled rules-config JSON to storage when the blobs are absent.
/// </summary>
public sealed class RulesConfigSeederTests : IDisposable
{
    private const string RulesBlobName = "rules.json";
    private const string LookupsBlobName = "country-languages.json";
    private const string RulesContent = """{ "version": "v1.0", "outcomes": [] }""";
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

    [Fact]
    public async Task StartAsync_SeedsBothBlobs_FromBundledFiles_WhenMissing()
    {
        WriteSeedFile(RulesBlobName, RulesContent);
        WriteSeedFile(LookupsBlobName, LookupsContent);
        _store.ReadAsync(Arg.Any<RulesConfigType>(), Arg.Any<CancellationToken>())
            .Throws(new RulesConfigNotFoundException("absent"));

        await NewSeeder().StartAsync(CancellationToken.None);

        await _store.Received(1).WriteAsync(RulesConfigType.Rules, RulesContent, null, Arg.Any<CancellationToken>());
        await _store.Received(1).WriteAsync(RulesConfigType.Lookups, LookupsContent, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_DoesNotOverwrite_WhenBlobAlreadyPresent()
    {
        WriteSeedFile(RulesBlobName, RulesContent);
        WriteSeedFile(LookupsBlobName, LookupsContent);
        _store.ReadAsync(Arg.Any<RulesConfigType>(), Arg.Any<CancellationToken>())
            .Returns(new RulesConfigBlob("existing", "etag-1"));

        await NewSeeder().StartAsync(CancellationToken.None);

        await _store.DidNotReceive().WriteAsync(
            Arg.Any<RulesConfigType>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_SkipsWriteAndDoesNotThrow_WhenSeedFileMissing()
    {
        // Seed dir is empty (no files written) — nothing to upload.
        _store.ReadAsync(Arg.Any<RulesConfigType>(), Arg.Any<CancellationToken>())
            .Throws(new RulesConfigNotFoundException("absent"));

        await NewSeeder().StartAsync(CancellationToken.None);

        await _store.DidNotReceive().WriteAsync(
            Arg.Any<RulesConfigType>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_SwallowsConflict_FromConcurrentSeed()
    {
        WriteSeedFile(RulesBlobName, RulesContent);
        WriteSeedFile(LookupsBlobName, LookupsContent);
        _store.ReadAsync(Arg.Any<RulesConfigType>(), Arg.Any<CancellationToken>())
            .Throws(new RulesConfigNotFoundException("absent"));
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
}
