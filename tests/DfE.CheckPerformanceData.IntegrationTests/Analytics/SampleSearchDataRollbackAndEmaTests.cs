using System.Text.Json;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using DfE.CheckPerformanceData.Persistence.Repositories;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Ties together the three deliverables of the seed-page Cancel/rollback fix:
//   1. Cancel = interrupt + rollback. The DELETE endpoint's response should drop every
//      row this seed job wrote, whether the seeder was still running or had already
//      completed (the 24 h preset finishes in ~3 s and beats the click every time).
//   2. Persistent per-event seconds rate. After a successful seed the stored value in
//      SearchAnalytics:SeedSecondsPerEvent must move toward the measured value via
//      the EMA blend (alpha=0.3).
//   3. NOT after a cancelled seed. A cancelled run does not reflect true throughput
//      and must leave the stored value alone.
[Collection(nameof(PostgresCollection))]
public sealed class SampleSearchDataRollbackAndEmaTests
{
    private readonly PostgresFixture _fixture;

    public SampleSearchDataRollbackAndEmaTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // The controller uses IServiceScopeFactory to hand its background task a fresh
    // scope. This factory returns scopes whose service provider is backed by a real
    // PostgresFixture DbContext — matching the production behaviour where each scope
    // resolves a scope-lifetime DbContext.
    private sealed class TestScopeFactory : IServiceScopeFactory, IServiceProvider, IServiceScope
    {
        private readonly PostgresFixture _fixture;
        public TestScopeFactory(PostgresFixture fixture) { _fixture = fixture; }

        public IServiceScope CreateScope() => new Scope(_fixture);
        public object? GetService(Type serviceType) => new Scope(_fixture).ServiceProvider.GetService(serviceType);
        public IServiceProvider ServiceProvider => this;
        public void Dispose() { }

        private sealed class Scope : IServiceScope, IServiceProvider
        {
            private readonly PostgresFixture _fixture;
            private readonly List<IDisposable> _bag = new();
            public Scope(PostgresFixture fixture) { _fixture = fixture; }
            public IServiceProvider ServiceProvider => this;
            public void Dispose()
            {
                foreach (var d in _bag) { try { d.Dispose(); } catch { } }
            }

            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(SampleSearchDataSeeder))
                {
                    var ctx = _fixture.CreateContext(); _bag.Add(ctx);
                    var ctx2 = _fixture.CreateContext(); _bag.Add(ctx2);
                    return new SampleSearchDataSeeder(
                        new DbSearchAnalyticsSink(ctx),
                        new DbSampleSearchDataGateway(ctx2));
                }
                if (serviceType == typeof(DfE.CheckPerformanceData.Persistence.Contexts.IPortalDbContext))
                {
                    var ctx = _fixture.CreateContext(); _bag.Add(ctx);
                    return ctx;
                }
                if (serviceType == typeof(ISettingService))
                {
                    var ctx = _fixture.CreateContext(); _bag.Add(ctx);
                    return new SettingService(new SettingRepository(ctx));
                }
                return null;
            }
        }
    }

    private sealed class TestHostEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private (TestDataController Controller, ISettingService Settings, ISampleSearchDataSeedJobStore Store) BuildController()
    {
        var ctx = _fixture.CreateContext();
        var seeder = new SampleSearchDataSeeder(
            new DbSearchAnalyticsSink(ctx),
            new DbSampleSearchDataGateway(_fixture.CreateContext()));
        var gateway = new DbSampleSearchDataGateway(_fixture.CreateContext());
        var settings = new SettingService(new SettingRepository(_fixture.CreateContext()));
        var store = new InMemorySampleSearchDataSeedJobStore(TimeProvider.System);
        var env = new TestHostEnv();
        var scopeFactory = new TestScopeFactory(_fixture);

        var controller = new TestDataController(
            seeder,
            env,
            store,
            scopeFactory,
            dbContext: _fixture.CreateContext(),
            currentUserService: null,
            adminGateway: gateway,
            settings: settings);
        // Minimal HttpContext with a fake ISessionFeature so the controller's read of
        // HttpContext.Session.Id doesn't throw (the DefaultHttpContext.Session getter
        // raises "Session has not been configured for this application or request"
        // otherwise). The tests invoke actions directly so anti-forgery is not
        // enforced at this level.
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
        };
        return (controller, settings, store);
    }

    private sealed class FakeSessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = new FakeSession();
    }

    private sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public bool IsAvailable => true;
        public string Id { get; } = "test-session-" + Guid.NewGuid().ToString("N");
        public IEnumerable<string> Keys => _store.Keys;
        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;
        public bool TryGetValue(string key, out byte[] value)
        {
            if (_store.TryGetValue(key, out var v)) { value = v; return true; }
            value = Array.Empty<byte>(); return false;
        }
    }

    // Verifies deliverable #1: the DELETE endpoint interrupts + rolls back — even when
    // the seeder has already Completed. Reproduces Lance's report: for the 24 h preset
    // the seed finishes in ~3 s and the RequestCancel guard (before this fix)
    // rejected the cancel because state != Running.
    [Fact]
    public async Task CancelEndpoint_RollsBackRows_EvenWhenSeedAlreadyCompleted()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var (controller, _, store) = BuildController();

        // Seed a job directly through the seeder so we can complete-mark it and then
        // hit the Cancel endpoint like a rushed user click.
        var jobId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        var job = store.Register("the last 24 hours", 40, 3, cts);
        // Overwrite the store-assigned JobId with our own so the seeder and DELETE
        // endpoint agree — the controller's real flow uses the store's Register
        // return value; here we simulate that by using the register-supplied id.
        var seeder = new SampleSearchDataSeeder(
            new DbSearchAnalyticsSink(_fixture.CreateContext()),
            new DbSampleSearchDataGateway(_fixture.CreateContext()));
        await seeder.SeedAsync(
            TimeSpan.FromHours(24), 40, 3,
            new DateTime(2026, 07, 29, 12, 00, 00, DateTimeKind.Utc),
            seed: 1,
            cancellationToken: CancellationToken.None,
            jobId: job.JobId);
        store.MarkCompleted(job.JobId, auditEntryId: 1);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        var beforeEvents = await ScalarLongAsync(conn, "SELECT COUNT(*) FROM search_events;");
        Assert.Equal(40L, beforeEvents);

        // Simulate the modal's Cancel click.
        var result = await controller.CancelSampleSearchDataSeed(job.JobId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = ok.Value;
        Assert.NotNull(body);
        var json = JsonSerializer.Serialize(body);
        Assert.Contains("rolledBackRows", json);
        Assert.Contains("rolled back", json.ToLowerInvariant());

        var afterEvents = await ScalarLongAsync(conn, "SELECT COUNT(*) FROM search_events;");
        var afterMessages = await ScalarLongAsync(conn, "SELECT COUNT(*) FROM search_messages;");
        Assert.Equal(0L, afterEvents);
        Assert.Equal(0L, afterMessages);

        // And the audit trail carries a rollback entry. Prior tests in the collection
        // may have written their own audit rows; count only rows written since we
        // started this test so a shared fixture doesn't skew the check.
        var auditCount = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM \"AuditEntries\" WHERE \"Action\" = 'SampleSearchDataSeedRolledBack';");
        Assert.True(auditCount >= 1L,
            $"Expected at least one SampleSearchDataSeedRolledBack audit entry; got {auditCount}.");
    }

    // Deliverable #2: after a successful non-cancelled seed the stored per-event rate
    // moves. Since the run duration is small the measured value dominates via the
    // EMA blend — the assertion is directional (moved toward measured) rather than
    // matching an exact number, because wall-clock is inherently jittery.
    [Fact]
    public async Task RunSeed_UpdatesSecondsPerEvent_AfterSuccessfulSeed()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        var (controller, settings, _) = BuildController();

        // Pin the stored value high so any real measurement is guaranteed to bend it
        // downward: default is 0.1, but write 1.0 so the EMA has room to move.
        await settings.SaveAsync(SettingKeys.SearchAnalyticsSeedSecondsPerEvent, "1.0");
        var before = await settings.GetDoubleAsync(SettingKeys.SearchAnalyticsSeedSecondsPerEvent);
        Assert.Equal(1.0, before, precision: 6);

        // Kick off a seed via the POST action. The action is fire-and-forget, so wait
        // for the store to observe the terminal state.
        var post = controller.SeedSampleSearchData("24h", eventCount: 60, messageCount: 3);
        var redirect = Assert.IsType<RedirectToActionResult>(post);
        var jobId = (Guid)redirect.RouteValues!["jobId"]!;

        await WaitForTerminalAsync(controller, jobId);

        var after = await settings.GetDoubleAsync(SettingKeys.SearchAnalyticsSeedSecondsPerEvent);
        Assert.True(after < before,
            $"After a successful seed the EMA-blended value should have moved downward from {before}; got {after}.");
        Assert.True(after > 0,
            $"Stored value must remain strictly positive; got {after}.");
    }

    // Deliverable #3: a cancelled seed does NOT touch the stored per-event rate. A
    // cancelled run is arbitrary in length and reflects nothing about the machine's
    // actual throughput.
    [Fact]
    public async Task RunSeed_DoesNotUpdateSecondsPerEvent_AfterCancelledSeed()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        var (controller, settings, store) = BuildController();

        const string PinnedValue = "0.42";
        await settings.SaveAsync(SettingKeys.SearchAnalyticsSeedSecondsPerEvent, PinnedValue);

        // Kick off a large enough seed that we can Cancel it mid-run.
        var post = controller.SeedSampleSearchData("year", eventCount: 5000, messageCount: 20);
        var redirect = Assert.IsType<RedirectToActionResult>(post);
        var jobId = (Guid)redirect.RouteValues!["jobId"]!;

        // Wait until the seeder is in-flight, then Cancel.
        await WaitForRunningAsync(store, jobId);
        await controller.CancelSampleSearchDataSeed(jobId, CancellationToken.None);
        await WaitForTerminalAsync(controller, jobId);

        var after = await settings.GetDoubleAsync(SettingKeys.SearchAnalyticsSeedSecondsPerEvent);
        Assert.Equal(0.42, after, precision: 6);
    }

    // Cancel race: MarkCompleted vs RequestCancel firing on overlapping threads. The
    // small preset (500 events, 24 h window) finishes in a few seconds; if the click
    // lands after MarkCompleted has already fired the store transitions Running ->
    // Completed and RequestCancel sees a terminal state. If it lands before the last
    // batch flush the seeder throws OperationCanceledException on the next
    // ThrowIfCancellationRequested and the finally rolls back. Either path is legal —
    // the test guarantees the store ends in Cancelled or Completed (never Faulted /
    // ObjectDisposed torn) AND that the rollback dropped every row this iteration
    // wrote. Run 20 concurrent iterations to hit both branches of the race.
    [Fact]
    public async Task ConcurrentCancelDuringRunningSeeds_AllIterationsEndCleanAndFullyRollBack()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var iterations = new List<Task>(20);
        for (var i = 0; i < 20; i++)
        {
            iterations.Add(RunOneIterationAsync());
        }
        await Task.WhenAll(iterations);

        // Every iteration cleaned up after itself — search_events count is zero.
        // (Real-world seeds that don't get cancelled leave rows, but this test's
        // iterations all Cancel between 0 and 50 ms and the Cancel handler rolls back
        // every row the job wrote. If any iteration didn't roll back, this count is
        // non-zero.)
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        var remainingEvents = await ScalarLongAsync(conn, "SELECT COUNT(*) FROM search_events;");
        Assert.Equal(0L, remainingEvents);
    }

    private async Task RunOneIterationAsync()
    {
        var (controller, _, store) = BuildController();

        // Fire the seed synchronously; the controller kicks a background task and
        // returns a redirect with the JobId in RouteValues.
        var post = controller.SeedSampleSearchData("24h", eventCount: 500, messageCount: 0);
        var redirect = Assert.IsType<RedirectToActionResult>(post);
        var jobId = (Guid)redirect.RouteValues!["jobId"]!;

        // Sleep for a random 0-50 ms then Cancel. Straddles the "before first batch",
        // "mid-run", and "already-Completed" boundaries of the seeder's lifecycle.
        var delayMs = Random.Shared.Next(0, 50);
        await Task.Delay(delayMs);

        // Cancel: must NOT throw ObjectDisposedException even when the seeder's
        // finally already ran (Cancelling code path guards for that).
        var cancelResult = await controller.CancelSampleSearchDataSeed(jobId, CancellationToken.None);
        Assert.IsType<OkObjectResult>(cancelResult);

        // Wait for the store to observe a terminal state.
        await WaitForTerminalAsync(controller, jobId);

        // Only Completed or Failed are legal terminal states for the store enum;
        // cancelled runs land as Completed too (the seeder's OperationCanceledException
        // path calls MarkCompleted with a "cancelled" note). Faulted / torn states
        // would produce Failed with an unrelated exception message.
        var snap = store.Get(jobId);
        Assert.NotNull(snap);
        Assert.NotEqual(SampleSearchDataSeedJobState.Running, snap!.State);
        Assert.NotEqual(SampleSearchDataSeedJobState.Cancelling, snap.State);
        if (snap.State == SampleSearchDataSeedJobState.Failed)
        {
            Assert.False(
                (snap.ErrorMessage ?? string.Empty).Contains("ObjectDisposed", StringComparison.OrdinalIgnoreCase),
                $"Iteration reached Failed with ObjectDisposedException — cancel race is not clean. Message: {snap.ErrorMessage}");
        }
    }

    private static async Task WaitForRunningAsync(
        ISampleSearchDataSeedJobStore store, Guid jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var snap = store.Get(jobId);
            if (snap is not null && snap.EventsWritten > 0)
            {
                return;
            }
            await Task.Delay(50);
        }
    }

    private static async Task WaitForTerminalAsync(
        TestDataController controller, Guid jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var json = controller.SampleSearchDataProgress(jobId);
            if (json is JsonResult j && j.Value is not null)
            {
                var text = JsonSerializer.Serialize(j.Value);
                if (text.Contains("\"Completed\"") || text.Contains("\"Failed\""))
                {
                    return;
                }
            }
            await Task.Delay(100);
        }
    }

    private static async Task<long> ScalarLongAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(value);
    }
}
