using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Extensions;

// Environment predicates that gate destructive admin surfaces.
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
    // allowed to render / execute only in these environments:
    //
    //   Development — local docker-compose stack.
    //   Review      — per-PR ephemeral review app on AKS.
    //
    // Explicit denylist by omission: QA, Preproduction and Production always fall through
    // to the false branch. That is the point — nobody should be able to click "Delete all
    // search data" on a customer-facing sink and hope the button was disabled somewhere.
    public static bool IsSampleDataAdminEnvironment(this IHostEnvironment env) =>
        env.IsDevelopment() || env.IsEnvironment(ReviewEnvironmentName);
}
