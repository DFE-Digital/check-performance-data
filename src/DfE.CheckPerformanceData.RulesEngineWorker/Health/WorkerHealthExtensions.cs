using DfE.CheckPerformanceData.Infrastructure.RulesEngine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DfE.CheckPerformanceData.RulesEngineWorker.Health;

/// <summary>
/// Registers and maps the worker's HTTP health surface. Liveness reports only that the
/// process is up; readiness aggregates the data-plane checks (database/queue reachability
/// and the rules provider) so a crash-looping or DB-isolated worker is detected by the
/// orchestrator instead of being invisible until a deploy timeout.
/// </summary>
public static class WorkerHealthExtensions
{
    /// <summary>Endpoints carrying the readiness checks.</summary>
    public const string ReadyTag = "ready";

    public const string LivePath = "/healthz/live";
    public const string ReadyPath = "/healthz/ready";

    public static IServiceCollection AddWorkerHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<QueueHealthCheck>("queue", tags: new[] { ReadyTag })
            .AddCheck<RulesProviderHealthCheck>("rules-readiness", tags: new[] { ReadyTag });

        return services;
    }

    public static IEndpointRouteBuilder MapWorkerHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        // Liveness runs no checks: it answers solely on the process being able to serve a
        // request, so a transient database outage never restart-loops the container.
        endpoints.MapHealthChecks(LivePath, new HealthCheckOptions
        {
            Predicate = _ => false,
        });

        endpoints.MapHealthChecks(ReadyPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(ReadyTag),
        });

        return endpoints;
    }
}
