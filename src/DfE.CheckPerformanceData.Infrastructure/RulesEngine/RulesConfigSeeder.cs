using System.Globalization;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.RulesConfig;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Infrastructure.RulesEngine;

/// <summary>
/// On worker startup, ensures the rules-config blobs (<c>rules.json</c>,
/// <c>country-languages.json</c>) exist in storage by uploading the image-bundled seed copies
/// when they are absent.
///
/// Real Azure environments only get an <em>empty</em> <c>rules-config</c> container from
/// Terraform — the blob content is not provisioned by IaC, and the storage account has no
/// public network access, so a pipeline runner cannot easily upload it. Rather than require a
/// separate manual seeding step, the worker seeds itself: it already ships the seed JSON in its
/// image (copied next to the binary) and already has in-cluster network access to storage.
///
/// <c>country-languages.json</c> is strictly non-destructive: an existing blob is never
/// overwritten (the write uses If-None-Match=*), so admin edits always survive a restart.
///
/// <c>rules.json</c> is <em>version-gated</em>: when the bundled seed's <c>version</c>
/// (<c>yyyy.MM.dd-NN</c>) is strictly newer than the stored blob's, the seeder replaces the
/// blob — this is how a rule-schema change (e.g. a renamed catalogue field) reaches
/// environments whose stored config would otherwise fail validation forever. Admin saves are
/// stamped <c>yyyy.MM.dd-HHmmss</c>, so an edit made after the bundled seed's date always
/// compares newer and survives restarts; only edits older than a deliberately bumped seed are
/// superseded. Versions are compared structurally as (date, numeric suffix) — never as plain
/// strings, since the two suffix formats differ in width. The overwrite is If-Match guarded by
/// the ETag just read, so a concurrent admin save wins the race.
///
/// Best-effort: any storage error is logged and swallowed so a transient blip never blocks
/// worker startup — the provider's cold-fallback already covers a missing rule set. Registered
/// before <see cref="BlobRulesProvider"/> so its first synchronous load sees freshly-seeded
/// rules and reports Healthy immediately on a fresh environment.
/// </summary>
public sealed class RulesConfigSeeder : IHostedService
{
    private readonly IRulesConfigStore _store;
    private readonly BlobRulesProviderOptions _options;
    private readonly ILogger<RulesConfigSeeder> _logger;

    public RulesConfigSeeder(
        IRulesConfigStore store,
        IOptions<BlobRulesProviderOptions> options,
        ILogger<RulesConfigSeeder> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.SeedOnStartup)
        {
            _logger.LogDebug("Rules config seeding disabled (SeedOnStartup=false).");
            return;
        }

        var seedDir = _options.SeedDirectory is { Length: > 0 } dir
            ? dir
            : Path.Combine(AppContext.BaseDirectory, "seed");

        await SeedOrUpgradeRulesAsync(_options.RulesBlobName, seedDir, cancellationToken)
            .ConfigureAwait(false);
        await SeedIfMissingAsync(RulesConfigType.Lookups, _options.LookupsBlobName, seedDir, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedOrUpgradeRulesAsync(string blobName, string seedDir, CancellationToken ct)
    {
        var seedPath = Path.Combine(seedDir, blobName);
        var seedContent = await TryReadSeedFileAsync(seedPath, blobName, ct).ConfigureAwait(false);

        RulesConfigBlob? stored;
        try
        {
            stored = await _store.ReadAsync(RulesConfigType.Rules, ct).ConfigureAwait(false);
        }
        catch (RulesConfigNotFoundException)
        {
            // Expected on a fresh environment — fall through and seed it.
            stored = null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A non-404 storage error: don't risk clobbering, and don't block startup.
            _logger.LogError(ex,
                "Could not determine whether rules config '{Blob}' exists; skipping seed this startup.", blobName);
            return;
        }

        if (stored is null)
        {
            if (seedContent is null)
            {
                _logger.LogWarning(
                    "Rules config '{Blob}' is missing from storage and no bundled seed file was found at {Path}; " +
                    "the worker will run on cold-fallback until the blob is provisioned.", blobName, seedPath);
                return;
            }

            await WriteSeedAsync(RulesConfigType.Rules, blobName, seedPath, seedContent, expectedETag: null, ct)
                .ConfigureAwait(false);
            return;
        }

        // Blob present — only replace it when the bundled seed is strictly newer.
        if (seedContent is null) return;

        var bundledRaw = TryGetVersionString(seedContent);
        var bundled = ParseVersion(bundledRaw);
        if (bundled is null)
        {
            // A malformed seed must never clobber a live blob.
            _logger.LogWarning(
                "Bundled seed for rules config '{Blob}' has no parseable version ('{Version}'); " +
                "leaving the stored blob untouched.", blobName, bundledRaw);
            return;
        }

        var storedRaw = TryGetVersionString(stored.Content);
        var storedVersion = ParseVersion(storedRaw);

        // An unversioned/unparseable stored blob is treated as oldest: every admin save stamps
        // a parseable version, so it is not an admin artefact — restore the known-good seed.
        if (storedVersion is not null && bundled.Value.CompareTo(storedVersion.Value) <= 0)
        {
            _logger.LogInformation(
                "Rules config '{Blob}' is at version {StoredVersion} (bundled seed is {BundledVersion}); no upgrade needed.",
                blobName, storedRaw, bundledRaw);
            return;
        }

        _logger.LogInformation(
            "Upgrading rules config '{Blob}' from stored version {StoredVersion} to bundled version {BundledVersion}.",
            blobName, storedRaw ?? "(none)", bundledRaw);
        await WriteSeedAsync(RulesConfigType.Rules, blobName, seedPath, seedContent, expectedETag: stored.ETag, ct)
            .ConfigureAwait(false);
    }

    private async Task SeedIfMissingAsync(
        RulesConfigType type, string blobName, string seedDir, CancellationToken ct)
    {
        bool exists;
        try
        {
            await _store.ReadAsync(type, ct).ConfigureAwait(false);
            exists = true;
        }
        catch (RulesConfigNotFoundException)
        {
            // Expected on a fresh environment — fall through and seed it.
            exists = false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A non-404 storage error: don't risk clobbering, and don't block startup.
            _logger.LogError(ex,
                "Could not determine whether rules config '{Blob}' exists; skipping seed this startup.", blobName);
            return;
        }

        if (exists)
        {
            _logger.LogInformation("Rules config '{Blob}' already present; skipping seed.", blobName);
            return;
        }

        var seedPath = Path.Combine(seedDir, blobName);
        var content = await TryReadSeedFileAsync(seedPath, blobName, ct).ConfigureAwait(false);
        if (content is null)
        {
            _logger.LogWarning(
                "Rules config '{Blob}' is missing from storage and no bundled seed file was found at {Path}; " +
                "the worker will run on cold-fallback until the blob is provisioned.", blobName, seedPath);
            return;
        }

        await WriteSeedAsync(type, blobName, seedPath, content, expectedETag: null, ct).ConfigureAwait(false);
    }

    private async Task<string?> TryReadSeedFileAsync(string seedPath, string blobName, CancellationToken ct)
    {
        if (!File.Exists(seedPath)) return null;
        try
        {
            return await File.ReadAllTextAsync(seedPath, ct).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to read bundled seed file {Path}; skipping seed of '{Blob}'.", seedPath, blobName);
            return null;
        }
    }

    private async Task WriteSeedAsync(
        RulesConfigType type, string blobName, string seedPath, string content, string? expectedETag, CancellationToken ct)
    {
        try
        {
            // expectedETag=null → If-None-Match=* → creates the blob only if it is still absent.
            // A concrete ETag → If-Match → upgrades only if nobody wrote since we read.
            await _store.WriteAsync(type, content, expectedETag, ct).ConfigureAwait(false);
            _logger.LogInformation("Seeded rules config '{Blob}' from bundled {Path}.", blobName, seedPath);
        }
        catch (RulesConfigConflictException)
        {
            // Another instance seeded it first, or an admin saved between our read and write.
            // Either way the blob now has fresher content; the next restart re-evaluates.
            _logger.LogInformation(
                "Rules config '{Blob}' was written concurrently by another writer; nothing to do.", blobName);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed rules config '{Blob}'; the worker will run on cold-fallback.", blobName);
        }
    }

    /// <summary>Extracts the root <c>version</c> string from a config JSON document, or null.</summary>
    private static string? TryGetVersionString(string json)
    {
        try
        {
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            using var doc = JsonDocument.Parse(json, options);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Name.Equals("version", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a config version into its comparable parts. Seeds use <c>yyyy.MM.dd-NN</c> and
    /// admin saves <c>yyyy.MM.dd-HHmmss</c>, so the suffix must compare numerically — an
    /// ordinal string compare would rank <c>-99</c> above <c>-134502</c> on the same day.
    /// A missing suffix parses as 0; anything else unparseable returns null.
    /// </summary>
    private static (DateOnly Date, long Sequence)? ParseVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var trimmed = version.Trim();

        var dash = trimmed.IndexOf('-');
        var datePart = dash < 0 ? trimmed : trimmed[..dash];
        var suffixPart = dash < 0 ? string.Empty : trimmed[(dash + 1)..];

        if (!DateOnly.TryParseExact(datePart, "yyyy.MM.dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;

        if (suffixPart.Length == 0) return (date, 0);
        if (!long.TryParse(suffixPart, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
            return null;

        return (date, sequence);
    }
}
