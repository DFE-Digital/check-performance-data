using Community.Microsoft.Extensions.Caching.PostgreSql;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace DfE.CheckPerformanceData.Web.Startup;

public static class SessionStoreExtensions
{
    public static WebApplicationBuilder AddCpdSessionStore(this WebApplicationBuilder builder)
    {
        // Session must be backed by a store shared across all web replicas. Production runs multiple
        // pods, so the default AddDistributedMemoryCache (per-pod, despite the name) would lose the
        // in-progress journey RequestState whenever a follow-up request load-balances to a different
        // pod — the user gets bounced back to Check Your Pupil Data. Back it with the PostgreSQL we
        // already run instead. CreateInfrastructure lets the cache table be created on startup; the
        // app already self-migrates with this same connection (see MigrateDatabaseAsync), so the DB
        // user has the required rights, and the create is idempotent across concurrently-starting pods.
        builder.Services.AddDistributedPostgreSqlCache(options =>
        {
            options.ConnectionString = builder.Configuration.GetConnectionString("Postgres");
            options.SchemaName = "public";
            options.TableName = "session_cache";
            options.CreateInfrastructure = true;
        });
        builder.Services.AddCpdSession(builder.Configuration, builder.Environment);

        return builder;
    }
}
