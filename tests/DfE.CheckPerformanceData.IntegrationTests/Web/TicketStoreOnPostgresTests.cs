using System.Security.Claims;
using Community.Microsoft.Extensions.Caching.PostgreSql;
using DfE.CheckPerformanceData.Infrastructure.Authentication;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DfE.CheckPerformanceData.IntegrationTests.Web;

// Drives the ticket store over the cache implementation production actually uses.
//
// Every other test in this area runs against MemoryDistributedCache, and the two do not agree:
// PostgreSqlCache validates the absolute expiry and rejects one that is not in the future, where
// the in-memory cache accepts it silently. A store that only ever meets the in-memory cache can
// therefore be green in CI and fail on the first real sign-in. These tests exist so the
// production path is exercised at least once.
[Collection(nameof(PostgresCollection))]
public sealed class TicketStoreOnPostgresTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    [Fact]
    public async Task ATicketRoundTripsThroughPostgres()
    {
        var store = NewStore();
        var key = await store.StoreAsync(NewTicket("someone@example.gov.uk"));

        var retrieved = await store.RetrieveAsync(key);

        Assert.Equal(
            "someone@example.gov.uk",
            retrieved!.Principal.FindFirst(ClaimTypes.Email)?.Value);
    }

    // Sliding expiration renews in place. Postgres is an upsert rather than an in-memory
    // overwrite, so this is worth pinning against the real implementation.
    [Fact]
    public async Task RenewUpdatesTheSameRow()
    {
        var store = NewStore();
        var key = await store.StoreAsync(NewTicket("before@example.gov.uk"));

        var renewed = NewTicket("before@example.gov.uk");
        renewed.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2);
        await store.RenewAsync(key, renewed);

        var retrieved = await store.RetrieveAsync(key);
        Assert.Equal("before@example.gov.uk", retrieved!.Principal.FindFirst(ClaimTypes.Email)?.Value);
    }

    [Fact]
    public async Task RemoveDeletesTheRow()
    {
        var store = NewStore();
        var key = await store.StoreAsync(NewTicket("someone@example.gov.uk"));

        await store.RemoveAsync(key);

        Assert.Null(await store.RetrieveAsync(key));
    }

    // The divergence that motivated this class: PostgreSqlCache throws
    // "The absolute expiration value must be in the future" for an expiry that has passed.
    // The store clamps, so this must not throw.
    [Fact]
    public async Task AnAlreadyExpiredTicketDoesNotBlowUpTheWrite()
    {
        var store = NewStore();
        var ticket = NewTicket("someone@example.gov.uk");
        ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-5);

        var key = await store.StoreAsync(ticket);

        // Whether it survives to be read back is immaterial — the point is that the write is
        // accepted rather than throwing out of the sign-in path.
        Assert.StartsWith("auth-ticket:", key, StringComparison.Ordinal);
    }

    // Ticket rows share the cache table with MVC session state.
    [Fact]
    public async Task TicketKeysDoNotCollideWithSessionKeys()
    {
        var cache = NewCache();
        var store = NewStore(cache);

        await cache.SetStringAsync("some-session-id", "session state");
        var key = await store.StoreAsync(NewTicket("someone@example.gov.uk"));

        Assert.NotNull(await store.RetrieveAsync(key));
        Assert.Equal("session state", await cache.GetStringAsync("some-session-id"));
    }

    private IDistributedCache NewCache()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDistributedPostgreSqlCache(options =>
        {
            options.ConnectionString = _fixture.ConnectionString;
            options.SchemaName = "public";
            options.TableName = "session_cache";
            options.CreateInfrastructure = true;
        });
        return services.BuildServiceProvider().GetRequiredService<IDistributedCache>();
    }

    private DistributedCacheTicketStore NewStore(IDistributedCache? cache = null) =>
        new(cache ?? NewCache(),
            new EphemeralDataProtectionProvider(),
            NullLogger<DistributedCacheTicketStore>.Instance);

    private static AuthenticationTicket NewTicket(string email)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, email), new Claim(ClaimTypes.NameIdentifier, email)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30) },
            CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
