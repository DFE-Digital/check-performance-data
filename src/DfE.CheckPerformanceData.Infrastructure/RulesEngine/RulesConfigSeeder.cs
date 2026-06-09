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
/// Idempotent and non-destructive: an existing blob is never overwritten (the write uses
/// If-None-Match=*), so edits made through the admin editor always survive a restart. Best-effort:
/// any storage error is logged and swallowed so a transient blip never blocks worker startup —
/// the provider's cold-fallback already covers a missing rule set. Registered before
/// <see cref="BlobRulesProvider"/> so its first synchronous load sees freshly-seeded rules and
/// reports Healthy immediately on a fresh environment.
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

        await SeedIfMissingAsync(RulesConfigType.Rules, _options.RulesBlobName, seedDir, cancellationToken)
            .ConfigureAwait(false);
        await SeedIfMissingAsync(RulesConfigType.Lookups, _options.LookupsBlobName, seedDir, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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
        if (!File.Exists(seedPath))
        {
            _logger.LogWarning(
                "Rules config '{Blob}' is missing from storage and no bundled seed file was found at {Path}; " +
                "the worker will run on cold-fallback until the blob is provisioned.", blobName, seedPath);
            return;
        }

        string content;
        try
        {
            content = await File.ReadAllTextAsync(seedPath, ct).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to read bundled seed file {Path}; skipping seed of '{Blob}'.", seedPath, blobName);
            return;
        }

        try
        {
            // expectedETag=null → If-None-Match=* → creates the blob only if it is still absent.
            await _store.WriteAsync(type, content, expectedETag: null, ct).ConfigureAwait(false);
            _logger.LogInformation("Seeded rules config '{Blob}' from bundled {Path}.", blobName, seedPath);
        }
        catch (RulesConfigConflictException)
        {
            // Another instance seeded it first (or it appeared between the read and the write).
            // That is exactly the desired end state, so treat it as success.
            _logger.LogInformation(
                "Rules config '{Blob}' was seeded concurrently by another instance; nothing to do.", blobName);
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
}
