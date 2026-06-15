using System.Text.Json;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.RulesEngine.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Infrastructure.RulesEngine;

/// <summary>
/// Loads <c>rules.json</c> and <c>country-languages.json</c> from blob storage,
/// validates the result, and exposes the current
/// <see cref="RulesSnapshot"/> via <see cref="IRulesProvider.Current"/>. Hosts
/// a periodic refresh that consults Blob ETags so unchanged blobs cost nothing.
///
/// The hot path (<see cref="Current"/>) is a single volatile read — refresh
/// installs a new immutable snapshot via <see cref="Interlocked.Exchange{T}(ref T, T)"/>
/// so no message ever observes a half-applied update.
///
/// On the first-load failure path the provider installs the cold fallback
/// (every outcome → Scrutiny) so the worker can start serving traffic; once
/// a real load succeeds the snapshot is replaced and health returns to Healthy.
/// </summary>
public sealed class BlobRulesProvider : IRulesProvider, IHostedService, IAsyncDisposable
{
    private readonly IRulesBlobReader _reader;
    private readonly RuleSetValidator _validator;
    private readonly BlobRulesProviderOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BlobRulesProvider> _logger;

    private RulesSnapshot _current;
    private string? _lastRulesETag;
    private string? _lastLookupsETag;
    private DateTimeOffset _lastSuccessfulRefresh;
    private CancellationTokenSource? _stopCts;
    private Task? _backgroundTask;

    public BlobRulesProvider(
        IRulesBlobReader reader,
        RuleSetValidator validator,
        IOptions<BlobRulesProviderOptions> options,
        TimeProvider timeProvider,
        ILogger<BlobRulesProvider> logger)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _current = ColdFallbackRules.Build(_timeProvider.GetUtcNow());
    }

    public RulesSnapshot Current => Volatile.Read(ref _current);

    // --- IHostedService ---

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // First refresh runs synchronously so the worker doesn't dequeue until
        // we've at least tried to load real rules. Failures degrade to cold fallback
        // (already installed in the constructor); the loop will keep retrying.
        await RefreshAsync(cancellationToken).ConfigureAwait(false);

        _stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _backgroundTask = Task.Run(() => RefreshLoopAsync(_stopCts.Token), CancellationToken.None);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _stopCts?.Cancel();
        if (_backgroundTask is not null)
        {
            try
            {
                await _backgroundTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* expected on shutdown */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Idempotent: the provider is registered both as a singleton and as an
        // IHostedService factory returning the same instance, so the container
        // disposes it twice on shutdown.
        var cts = Interlocked.Exchange(ref _stopCts, null);
        if (cts is not null)
        {
            cts.Cancel();
            cts.Dispose();
        }

        var task = Interlocked.Exchange(ref _backgroundTask, null);
        if (task is not null)
        {
            try { await task.ConfigureAwait(false); }
            catch { /* swallow on dispose */ }
        }
    }

    // --- core refresh ---

    /// <summary>
    /// Single refresh attempt. Public so tests (and integration tests) can drive
    /// it directly without waiting on <see cref="PeriodicTimer"/>.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var rulesFetch = await _reader.FetchAsync(_options.RulesBlobName, _lastRulesETag, cancellationToken)
                .ConfigureAwait(false);
            var lookupsFetch = await _reader.FetchAsync(_options.LookupsBlobName, _lastLookupsETag, cancellationToken)
                .ConfigureAwait(false);

            if (rulesFetch.IsError || lookupsFetch.IsError)
            {
                HandleRefreshFailure(rulesFetch, lookupsFetch);
                return;
            }

            // Both 304 → nothing changed.
            if (rulesFetch.IsNotModified && lookupsFetch.IsNotModified)
            {
                _lastSuccessfulRefresh = _timeProvider.GetUtcNow();
                return;
            }

            // To swap atomically we need both halves. If one came back NotModified,
            // re-use the existing snapshot's corresponding piece.
            var rulesContent = rulesFetch.IsSuccess ? rulesFetch.Content! : null;
            var lookupsContent = lookupsFetch.IsSuccess ? lookupsFetch.Content! : null;

            RuleSet? parsedRules;
            if (rulesContent is not null)
            {
                try
                {
                    parsedRules = JsonSerializer.Deserialize<RuleSet>(rulesContent, RulesJson.Options);
                }
                catch (JsonException jx)
                {
                    _logger.LogError(jx, "Rules JSON failed to parse; keeping previous snapshot {Version}.", _current.Version);
                    MarkStaleIfPast();
                    return;
                }

                if (parsedRules is null)
                {
                    _logger.LogError("Rules JSON deserialised to null; keeping previous snapshot {Version}.", _current.Version);
                    MarkStaleIfPast();
                    return;
                }

                var validation = _validator.Validate(parsedRules);
                if (!validation.IsValid)
                {
                    foreach (var err in validation.Errors)
                    {
                        _logger.LogError("Rules validation error: {Error}", err);
                    }
                    _logger.LogError("Rules validation failed ({Count} errors); keeping previous snapshot {Version}.",
                        validation.Errors.Count, _current.Version);
                    MarkStaleIfPast();
                    return;
                }
                parsedRules = validation.ResolvedRules!;
            }
            else
            {
                parsedRules = _current.Rules;
            }

            Lookups parsedLookups;
            if (lookupsContent is not null)
            {
                try
                {
                    // Underscore-prefixed keys are comments by convention (the seed
                    // file ships a "_comment" string entry), so deserialise loosely
                    // and keep only the real code → languages entries.
                    var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                        lookupsContent, RulesJson.Options);
                    parsedLookups = raw is null
                        ? Lookups.Empty
                        : new Lookups(raw
                            .Where(kvp => !kvp.Key.StartsWith('_') && kvp.Value.ValueKind == JsonValueKind.Array)
                            .ToDictionary(
                                kvp => kvp.Key,
                                kvp => (IReadOnlyList<string>)kvp.Value.EnumerateArray()
                                    .Select(e => e.GetString() ?? string.Empty)
                                    .ToArray()));
                }
                catch (JsonException jx)
                {
                    _logger.LogError(jx, "Lookups JSON failed to parse; keeping previous snapshot {Version}.", _current.Version);
                    MarkStaleIfPast();
                    return;
                }
            }
            else
            {
                parsedLookups = _current.Lookups;
            }

            var now = _timeProvider.GetUtcNow();
            var newSnapshot = new RulesSnapshot(
                Rules: parsedRules,
                Lookups: parsedLookups,
                Version: parsedRules.Version,
                LoadedAt: now,
                Health: RulesHealth.Healthy);

            var previous = Interlocked.Exchange(ref _current, newSnapshot);
            _lastRulesETag = rulesFetch.IsSuccess ? rulesFetch.ETag : _lastRulesETag;
            _lastLookupsETag = lookupsFetch.IsSuccess ? lookupsFetch.ETag : _lastLookupsETag;
            _lastSuccessfulRefresh = now;

            _logger.LogInformation(
                "Rules loaded: version={NewVersion} previous={PrevVersion} health=Healthy",
                newSnapshot.Version, previous.Version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during rules refresh; keeping previous snapshot {Version}.", _current.Version);
            MarkStaleIfPast();
        }
    }

    private void HandleRefreshFailure(RulesBlobFetch rulesFetch, RulesBlobFetch lookupsFetch)
    {
        if (rulesFetch.IsError)
            _logger.LogError("Rules blob fetch error: {Reason}", rulesFetch.ErrorReason);
        if (lookupsFetch.IsError)
            _logger.LogError("Lookups blob fetch error: {Reason}", lookupsFetch.ErrorReason);
        MarkStaleIfPast();
    }

    private void MarkStaleIfPast()
    {
        // If we've never had a successful refresh, leave Health at its current value
        // (which is ColdFallback from the constructor) — don't accidentally upgrade.
        if (_current.Health == RulesHealth.ColdFallback) return;

        var elapsed = _timeProvider.GetUtcNow() - _lastSuccessfulRefresh;
        if (elapsed.TotalSeconds < _options.StalenessWarningSeconds) return;
        if (_current.Health == RulesHealth.StaleLastKnownGood) return;

        var stale = _current with { Health = RulesHealth.StaleLastKnownGood };
        Interlocked.Exchange(ref _current, stale);
        _logger.LogWarning(
            "Rules refresh has been failing for {Elapsed:N0}s; marking snapshot {Version} as StaleLastKnownGood.",
            elapsed.TotalSeconds, stale.Version);
    }

    private async Task RefreshLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(_options.RefreshIntervalSeconds),
            _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RefreshAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
    }
}
