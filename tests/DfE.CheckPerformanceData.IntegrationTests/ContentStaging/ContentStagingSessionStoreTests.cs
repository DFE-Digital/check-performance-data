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

        Assert.Equal(json, await store.GetBundleJsonAsync(id, "editor@education.gov.uk"));
    }

    // A preview session belongs to the administrator who uploaded it. The whole point of the
    // preview step is that the person who confirms an import has seen what it will do; letting
    // anyone else redeem the id turns "approve what you reviewed" into "approve what somebody
    // else reviewed". Both parties hold the same grant, so this is not a privilege boundary —
    // it is the integrity of the review step, and the owner is already recorded.
    [Fact]
    public async Task Get_ByADifferentUser_ReturnsNull()
    {
        await ResetAsync();
        await using var ctx = _fixture.CreateContext();
        var store = new ContentStagingSessionStore(ctx);

        var id = await store.CreateAsync("{\"mine\":true}", "alice@education.gov.uk");

        Assert.Null(await store.GetBundleJsonAsync(id, "bob@education.gov.uk"));
        Assert.Equal("{\"mine\":true}", await store.GetBundleJsonAsync(id, "alice@education.gov.uk"));
    }

    // Addresses are clipped to the column width on the way in, so the comparison has to clip
    // the same way or an improbably long address could never redeem its own session.
    [Fact]
    public async Task Get_ByAnOverlongOwner_StillMatchesItsOwnSession()
    {
        await ResetAsync();
        await using var ctx = _fixture.CreateContext();
        var store = new ContentStagingSessionStore(ctx);
        var longEmail = new string('a', 250) + "@education.gov.uk";

        var id = await store.CreateAsync("{}", longEmail);

        Assert.NotNull(await store.GetBundleJsonAsync(id, longEmail));
    }

    // Nothing else bounds this table — the sweep only takes rows already past expiry, and it
    // runs before the insert — so without this an operator previewing repeatedly stores a copy
    // of the bundle every time and deletes none of them. At the upload ceiling that is enough
    // to fill the database's storage from a surface that needs no confirm step.
    [Fact]
    public async Task Create_ReplacesTheSameOperatorsPreviousSession()
    {
        await ResetAsync();
        await using var ctx = _fixture.CreateContext();
        var store = new ContentStagingSessionStore(ctx);

        var first = await store.CreateAsync("{\"n\":1}", "alice@education.gov.uk");
        var second = await store.CreateAsync("{\"n\":2}", "alice@education.gov.uk");

        Assert.Equal(1, await ctx.ContentStagingSessions.CountAsync());
        Assert.Null(await store.GetBundleJsonAsync(first, "alice@education.gov.uk"));
        Assert.Equal("{\"n\":2}", await store.GetBundleJsonAsync(second, "alice@education.gov.uk"));
    }

    // One operator starting a new preview must not cancel anybody else's.
    [Fact]
    public async Task Create_LeavesOtherOperatorsSessionsAlone()
    {
        await ResetAsync();
        await using var ctx = _fixture.CreateContext();
        var store = new ContentStagingSessionStore(ctx);

        var bobs = await store.CreateAsync("{\"whose\":\"bob\"}", "bob@education.gov.uk");
        await store.CreateAsync("{\"whose\":\"alice\"}", "alice@education.gov.uk");
        await store.CreateAsync("{\"whose\":\"alice2\"}", "alice@education.gov.uk");

        Assert.Equal("{\"whose\":\"bob\"}", await store.GetBundleJsonAsync(bobs, "bob@education.gov.uk"));
        Assert.Equal(2, await ctx.ContentStagingSessions.CountAsync());
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNull()
    {
        await ResetAsync();
        await using var ctx = _fixture.CreateContext();
        var store = new ContentStagingSessionStore(ctx);

        Assert.Null(await store.GetBundleJsonAsync(Guid.NewGuid(), null));
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

        Assert.Null(await store.GetBundleJsonAsync(id, "editor@education.gov.uk"));
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

        Assert.Equal("{\"a\":1}", await store.GetBundleJsonAsync(id, null));
    }

    [Fact]
    public async Task Delete_RemovesTheRow()
    {
        await ResetAsync();
        await using var ctx = _fixture.CreateContext();
        var store = new ContentStagingSessionStore(ctx);

        var id = await store.CreateAsync("{}", null);
        await store.DeleteAsync(id);

        Assert.Null(await store.GetBundleJsonAsync(id, null));
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
        Assert.Null(await store.GetBundleJsonAsync(stale1, null));
        Assert.Null(await store.GetBundleJsonAsync(stale2, null));
        Assert.Equal("{\"n\":3}", await store.GetBundleJsonAsync(live, null));
    }
}
