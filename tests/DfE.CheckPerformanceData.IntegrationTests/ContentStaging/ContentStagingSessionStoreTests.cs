using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.ContentStaging;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.ContentStaging;

// Advanceable clock — the codebase hand-rolls its fakes rather than taking a dependency on
// Microsoft.Extensions.TimeProvider.Testing, so this follows suit and adds the one thing the
// existing read-only fakes lack: the ability to step past an expiry.
file sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now = _now.Add(by);
}

// The session store is what replaced round-tripping the whole bundle through a hidden form
// field, so its contract is worth pinning against a real Postgres rather than a mock: a bundle
// survives the gap between Preview and Import intact, a stale one cannot be redeemed, and the
// rows do not accumulate.
[Collection(nameof(PostgresCollection))]
public sealed class ContentStagingSessionStoreTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private async Task ResetAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(@"TRUNCATE ""content_staging_sessions"";");
    }

    // A bundle big enough to have tripped the 4 MB form-value ceiling that motivated the whole
    // change — the case the old hidden-field round-trip could not carry.
    private static string LargeBundleJson() =>
        "{\"padding\":\"" + new string('x', 6 * 1024 * 1024) + "\"}";

    [Fact]
    public async Task Create_ThenGet_ReturnsTheBundleVerbatim()
    {
        await ResetAsync();
        await using var ctx = _fixture.CreateContext();
        var store = new ContentStagingSessionStore(ctx);
        var json = LargeBundleJson();

        var id = await store.CreateAsync(json, "editor@education.gov.uk");

        Assert.Equal(json, await store.GetBundleJsonAsync(id));
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNull()
    {
        await ResetAsync();
        await using var ctx = _fixture.CreateContext();
        var store = new ContentStagingSessionStore(ctx);

        Assert.Null(await store.GetBundleJsonAsync(Guid.NewGuid()));
    }

    // Expiry is enforced on read, not merely by the sweep, so a session cannot be redeemed
    // during the window between aging out and something getting round to deleting it.
    [Fact]
    public async Task Get_PastExpiry_ReturnsNull_EvenWithTheRowStillPresent()
    {
        await ResetAsync();
        await using var ctx = _fixture.CreateContext();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var store = new ContentStagingSessionStore(ctx, clock);

        var id = await store.CreateAsync("{}", "editor@education.gov.uk");
        clock.Advance(ContentStagingSessionDefaults.Lifetime + TimeSpan.FromMinutes(1));

        Assert.Null(await store.GetBundleJsonAsync(id));
        Assert.Equal(1, await ctx.ContentStagingSessions.CountAsync());
    }

    [Fact]
    public async Task Get_JustInsideExpiry_StillReturnsTheBundle()
    {
        await ResetAsync();
        await using var ctx = _fixture.CreateContext();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var store = new ContentStagingSessionStore(ctx, clock);

        var id = await store.CreateAsync("{\"a\":1}", null);
        clock.Advance(ContentStagingSessionDefaults.Lifetime - TimeSpan.FromMinutes(1));

        Assert.Equal("{\"a\":1}", await store.GetBundleJsonAsync(id));
    }

    [Fact]
    public async Task Delete_RemovesTheRow()
    {
        await ResetAsync();
        await using var ctx = _fixture.CreateContext();
        var store = new ContentStagingSessionStore(ctx);

        var id = await store.CreateAsync("{}", null);
        await store.DeleteAsync(id);

        Assert.Null(await store.GetBundleJsonAsync(id));
        Assert.Equal(0, await ctx.ContentStagingSessions.CountAsync());
    }

    // The sweep takes the expired rows and leaves the live one alone — an administrator part-way
    // through confirming an import does not lose their session because somebody else previewed.
    [Fact]
    public async Task PurgeExpired_DropsOnlyTheExpiredSessions()
    {
        await ResetAsync();
        await using var ctx = _fixture.CreateContext();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var store = new ContentStagingSessionStore(ctx, clock);

        var stale1 = await store.CreateAsync("{\"n\":1}", null);
        var stale2 = await store.CreateAsync("{\"n\":2}", null);
        clock.Advance(ContentStagingSessionDefaults.Lifetime + TimeSpan.FromMinutes(1));
        var live = await store.CreateAsync("{\"n\":3}", null);

        var purged = await store.PurgeExpiredAsync();

        Assert.Equal(2, purged);
        Assert.Null(await store.GetBundleJsonAsync(stale1));
        Assert.Null(await store.GetBundleJsonAsync(stale2));
        Assert.Equal("{\"n\":3}", await store.GetBundleJsonAsync(live));
    }
}
