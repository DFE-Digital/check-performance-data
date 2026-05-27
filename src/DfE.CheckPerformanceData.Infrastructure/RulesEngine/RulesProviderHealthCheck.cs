using DfE.CheckPerformanceData.Application.RulesEngine;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DfE.CheckPerformanceData.Infrastructure.RulesEngine;

/// <summary>
/// Translates the provider's <see cref="RulesHealth"/> into an ASP.NET Core
/// <see cref="HealthCheckResult"/>. Used to gate AKS readiness and to drive
/// the post-deploy health-check in the build-and-deploy pipeline.
/// </summary>
public sealed class RulesProviderHealthCheck : IHealthCheck
{
    private readonly IRulesProvider _provider;

    public RulesProviderHealthCheck(IRulesProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var snapshot = _provider.Current;
        var data = new Dictionary<string, object>
        {
            ["version"] = snapshot.Version,
            ["loadedAt"] = snapshot.LoadedAt,
            ["health"] = snapshot.Health.ToString(),
        };

        return Task.FromResult(snapshot.Health switch
        {
            RulesHealth.Healthy             => HealthCheckResult.Healthy($"Rules {snapshot.Version} loaded.", data),
            RulesHealth.StaleLastKnownGood  => HealthCheckResult.Degraded($"Rules {snapshot.Version} stale; refresh failing.", data: data),
            RulesHealth.ColdFallback        => HealthCheckResult.Unhealthy("Rules cold-fallback: no good copy loaded.", data: data),
            _                               => HealthCheckResult.Unhealthy($"Unknown rules health {snapshot.Health}.", data: data),
        });
    }
}
