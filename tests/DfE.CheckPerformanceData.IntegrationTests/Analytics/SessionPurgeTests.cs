using System.Reflection;
using System.Text.Json;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Integration coverage of the session-scoped purge action. Asserts the load-bearing
// transactional invariant: all three deletes (events + results via
// CASCADE + messages) AND the AuditEntry row must exist after commit, or none of them.
[Collection(nameof(PostgresCollection))]
public sealed class SessionPurgeTests
{
    private readonly PostgresFixture _fixture;

    public SessionPurgeTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Delete_KnownSession_RemovesEventsResultsMessagesAndWritesAudit()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        await TruncateAuditsAsync();
        const string sessionId = "session-to-purge";

        long parentEventId;
        await using (var seedCtx = _fixture.CreateContext())
        {
            var parent = new SearchEvent
            {
                OccurredAtUtc = DateTime.UtcNow.AddMinutes(-5),
                SessionId = sessionId,
                QueryRaw = "gone",
                QueryNormalised = "gone",
                ResultsPages = 1,
                ResultsBlocks = 0,
                LatencyMs = 12,
            };
            seedCtx.SearchEvents.Add(parent);
            await seedCtx.SaveChangesAsync();
            parentEventId = parent.Id;

            seedCtx.SearchEventResults.Add(new SearchEventResult
            {
                SearchEventId = parent.Id,
                Position = 1,
                ResultKind = "page",
                ResultKey = "/help/x",
                Rank = 0.5f,
            });
            seedCtx.SearchEventResults.Add(new SearchEventResult
            {
                SearchEventId = parent.Id,
                Position = 2,
                ResultKind = "page",
                ResultKey = "/help/y",
                Rank = 0.4f,
            });

            // Also seed a non-matching session so we prove scope isolation.
            seedCtx.SearchEvents.Add(new SearchEvent
            {
                OccurredAtUtc = DateTime.UtcNow.AddMinutes(-4),
                SessionId = "other-session",
                QueryRaw = "should survive",
                QueryNormalised = "should survive",
                ResultsPages = 1,
                ResultsBlocks = 0,
                LatencyMs = 5,
            });
            await seedCtx.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var messages = new DbSearchMessageService(context);
        await messages.CreateAsync(sessionId, "message 1", null, null, CancellationToken.None);
        await messages.CreateAsync(sessionId, "message 2", null, null, CancellationToken.None);
        await messages.CreateAsync("other-session", "keep me", null, null, CancellationToken.None);

        var query = new SearchAnalyticsQueryService(
                context,
                new StubSettingService(SettingKeys.SearchAnalyticsRetentionDays, 90));
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("acting-admin-sub");
        var controller = BuildController(context, query, messages, currentUser);

        var result = await controller.Delete(sessionId, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/Search/", redirect.Url);

        // Assert the three deletes.
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        Assert.Equal(0, await ScalarCountAsync(conn,
            "SELECT COUNT(*) FROM search_events WHERE session_id = @s;", ("s", sessionId)));
        Assert.Equal(0, await ScalarCountAsync(conn,
            "SELECT COUNT(*) FROM search_event_results WHERE search_event_id = @id;",
            ("id", parentEventId)));
        Assert.Equal(0, await ScalarCountAsync(conn,
            "SELECT COUNT(*) FROM search_messages WHERE session_id = @s;", ("s", sessionId)));

        // Non-matching session must survive.
        Assert.Equal(1, await ScalarCountAsync(conn,
            "SELECT COUNT(*) FROM search_events WHERE session_id = @s;", ("s", "other-session")));
        Assert.Equal(1, await ScalarCountAsync(conn,
            "SELECT COUNT(*) FROM search_messages WHERE session_id = @s;", ("s", "other-session")));

        // Audit row must exist with the correct shape.
        await using var auditCmd = conn.CreateCommand();
        auditCmd.CommandText = @"
            SELECT ""EntityType"", ""EntityId"", ""Action"", ""NewValues"", ""UserId""
            FROM ""AuditEntries""
            WHERE ""EntityType"" = 'SearchSession' AND ""EntityId"" = @s;";
        auditCmd.Parameters.AddWithValue("s", sessionId);
        await using var reader = await auditCmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Expected exactly one AuditEntry for the session purge.");
        Assert.Equal("SearchSession", reader.GetString(0));
        Assert.Equal(sessionId, reader.GetString(1));
        Assert.Equal("SearchSessionDelete", reader.GetString(2));
        var newValues = reader.GetString(3);
        Assert.Equal("acting-admin-sub", reader.GetString(4));

        using var payload = JsonDocument.Parse(newValues);
        var root = payload.RootElement;
        Assert.Equal(1, root.GetProperty("eventsDeleted").GetInt32());
        Assert.Equal(2, root.GetProperty("resultsDeleted").GetInt32());
        Assert.Equal(2, root.GetProperty("messagesDeleted").GetInt32());
        Assert.Equal("acting-admin-sub", root.GetProperty("deletedBy").GetString());
        Assert.False(await reader.ReadAsync(), "Expected only ONE audit row for the purge — got more.");
    }

    [Fact]
    public async Task Delete_SessionWithNothing_IsIdempotentReturnsRedirectAndWritesAuditOfZeros()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        await TruncateAuditsAsync();
        const string sessionId = "session-empty";

        await using var context = _fixture.CreateContext();
        var messages = new DbSearchMessageService(context);
        var query = new SearchAnalyticsQueryService(
                context,
                new StubSettingService(SettingKeys.SearchAnalyticsRetentionDays, 90));
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("acting-admin-sub");
        var controller = BuildController(context, query, messages, currentUser);

        var result = await controller.Delete(sessionId, CancellationToken.None);

        Assert.IsType<RedirectResult>(result);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ""NewValues"" FROM ""AuditEntries""
            WHERE ""EntityType"" = 'SearchSession' AND ""EntityId"" = @s;";
        cmd.Parameters.AddWithValue("s", sessionId);
        var raw = (string)(await cmd.ExecuteScalarAsync())!;
        using var payload = JsonDocument.Parse(raw);
        Assert.Equal(0, payload.RootElement.GetProperty("eventsDeleted").GetInt32());
        Assert.Equal(0, payload.RootElement.GetProperty("resultsDeleted").GetInt32());
        Assert.Equal(0, payload.RootElement.GetProperty("messagesDeleted").GetInt32());
    }

    // Transactional invariant: if the audit-entry write step throws inside
    // ExecuteInTransactionAsync, every side effect the controller made must roll back
    // — no partial delete without a matching audit row. Reproduces by injecting a
    // wrapper IPortalDbContext whose SaveChangesAsync throws only when the tracked
    // change set contains an AuditEntry with EntityType == "SearchSession" (i.e. the
    // moment the controller enrols the audit row). Events + messages must still be
    // present after the throw, and no AuditEntry may have landed.
    [Fact]
    public async Task Delete_WhenAuditWriteThrows_RollsBackEventsAndMessages_AndWritesNoAudit()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        await TruncateAuditsAsync();
        const string sessionId = "session-audit-throws";

        long parentEventId;
        await using (var seedCtx = _fixture.CreateContext())
        {
            var parent = new SearchEvent
            {
                OccurredAtUtc = DateTime.UtcNow.AddMinutes(-2),
                SessionId = sessionId,
                QueryRaw = "should-survive-rollback",
                QueryNormalised = "should-survive-rollback",
                ResultsPages = 1,
                ResultsBlocks = 0,
                LatencyMs = 8,
            };
            seedCtx.SearchEvents.Add(parent);
            await seedCtx.SaveChangesAsync();
            parentEventId = parent.Id;

            seedCtx.SearchEventResults.Add(new SearchEventResult
            {
                SearchEventId = parent.Id,
                Position = 1,
                ResultKind = "page",
                ResultKey = "/help/rollback",
                Rank = 0.5f,
            });
            await seedCtx.SaveChangesAsync();
        }

        // Seed a message on the same session so the messages purge inside the tx has
        // something to unwind.
        await using (var msgCtx = _fixture.CreateContext())
        {
            var messages = new DbSearchMessageService(msgCtx);
            await messages.CreateAsync(sessionId, "rollback me", null, null, CancellationToken.None);
        }

        await using var context = BuildContextWithAuditInterceptor(_fixture);
        var messageService = new DbSearchMessageService(context);
        var query = new SearchAnalyticsQueryService(
                context,
                new StubSettingService(SettingKeys.SearchAnalyticsRetentionDays, 90));
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns("acting-admin-sub");
        var controller = BuildController(context, query, messageService, currentUser);

        // The controller wraps its work in ExecuteInTransactionAsync. When our
        // interceptor throws on the audit-write step the whole tx rolls back and
        // the exception surfaces here.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.Delete(sessionId, CancellationToken.None));

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        // Events, results, messages ALL survive — the tx rolled everything back.
        Assert.Equal(1, await ScalarCountAsync(conn,
            "SELECT COUNT(*) FROM search_events WHERE session_id = @s;", ("s", sessionId)));
        Assert.Equal(1, await ScalarCountAsync(conn,
            "SELECT COUNT(*) FROM search_event_results WHERE search_event_id = @id;",
            ("id", parentEventId)));
        Assert.Equal(1, await ScalarCountAsync(conn,
            "SELECT COUNT(*) FROM search_messages WHERE session_id = @s;", ("s", sessionId)));

        // No audit entry landed. Count against the exact EntityType so a shared-fixture
        // audit row from another test doesn't skew the read.
        Assert.Equal(0, await ScalarCountAsync(conn,
            "SELECT COUNT(*) FROM \"AuditEntries\" WHERE \"EntityType\" = 'SearchSession' AND \"EntityId\" = @s;",
            ("s", sessionId)));
    }

    // Builds a PortalDbContext with an EF interceptor that raises on
    // SavingChangesAsync whenever the tracked change set contains an Added
    // AuditEntry for a SearchSession — the exact moment the controller is trying
    // to enrol the audit row inside its transaction. Any other SaveChangesAsync
    // call (seed setup, message service, etc.) passes through untouched.
    private static PortalDbContext BuildContextWithAuditInterceptor(PostgresFixture fixture)
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.EnableRetryOnFailure())
            .AddInterceptors(new ThrowingSearchSessionAuditInterceptor())
            .Options;
        return new PortalDbContext(options, new FakeCurrentUserService());
    }

    private sealed class ThrowingSearchSessionAuditInterceptor
        : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>>
            SavingChangesAsync(
                Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
                Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            var ctx = eventData.Context;
            if (ctx is not null)
            {
                var pendingAuditForSession = ctx.ChangeTracker.Entries<AuditEntry>()
                    .Any(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added
                              && e.Entity.EntityType == "SearchSession");
                if (pendingAuditForSession)
                {
                    throw new InvalidOperationException(
                        "Simulated failure at audit-write step — transaction must roll back.");
                }
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public void Delete_HasValidateAntiForgeryTokenAttribute()
    {
        var method = typeof(SearchAnalyticsController).GetMethod(nameof(SearchAnalyticsController.Delete))!;
        var attrs = method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true);
        Assert.NotEmpty(attrs);
    }

    [Fact]
    public void Delete_HasHttpPostAttributeOnSessionRoute()
    {
        var method = typeof(SearchAnalyticsController).GetMethod(nameof(SearchAnalyticsController.Delete))!;
        var http = method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true)
            .Cast<HttpPostAttribute>()
            .Single();
        Assert.Equal("Session/{id}/Delete", http.Template);
    }

    // ---- Helpers ----

    private static SearchAnalyticsController BuildController(
        IPortalDbContext dbContext,
        ISearchAnalyticsQueryService query,
        ISearchMessageService messages,
        ICurrentUserService currentUser)
    {
        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.CmsPageLength).Returns(20);

        var controller = new SearchAnalyticsController(
            query, settings, messages, dbContext, currentUser);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext, Substitute.For<ITempDataProvider>());
        return controller;
    }

    private async Task TruncateAuditsAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE \"AuditEntries\" RESTART IDENTITY;";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarCountAsync(NpgsqlConnection conn, string sql, params (string, object)[] parameters)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        var raw = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(raw);
    }
}
