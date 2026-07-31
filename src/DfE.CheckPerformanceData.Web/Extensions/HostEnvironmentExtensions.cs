using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Extensions;

// Environment predicates that gate destructive admin surfaces.
//
// Deployment ladder for this application (ASPNETCORE_ENVIRONMENT names — see
// terraform/application/config/*.yml):
//
//   Development   — every developer's local docker-compose stack.
//   Review        — per-PR ephemeral review app on AKS (short-lived).
//   QA            — the shared long-lived test environment on Azure. This is
//                   where the client's testers do their sit-down testing.
//   Preproduction — staging clone of production.
//   Production    — customer-facing live service.
//
// Deliberate whitelist (not `!IsProduction()`): a future environment tier — a new
// "Staging" ring, an on-prem UAT clone — is off by default rather than silently
// inheriting delete-all rights, and the review comes back to touch this file.
public static class HostEnvironmentExtensions
{
    // Environment names as they appear in ASPNETCORE_ENVIRONMENT across the deployments;
    // see terraform/application/config/*.yml.
    public const string ReviewEnvironmentName = "Review";
    public const string QaEnvironmentName = "QA";
    public const string PreproductionEnvironmentName = "Preproduction";

    // The seed-sample-search-data admin page + every destructive endpoint on it are
    // allowed to render / execute in every environment EXCEPT Preproduction and
    // Production:
    //
    //   Development — every developer's local stack.
    //   Review      — per-PR ephemeral review app on AKS.
    //   QA          — the shared long-lived Azure test environment.
    //
    // Explicit denylist by omission: Preproduction and Production always fall through
    // to false. Nobody should be able to click "Delete all search data" on a
    // customer-facing sink and hope the button was disabled somewhere else.
    public static bool IsSampleDataAdminEnvironment(this IHostEnvironment env) =>
        env.IsDevelopment()
        || env.IsEnvironment(ReviewEnvironmentName)
        || env.IsEnvironment(QaEnvironmentName);
}
