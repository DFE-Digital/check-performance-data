using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Controller-scope + view-render coverage of the session drill-in surface at
// /admin/Search/Session/{id}. The drill-in shows the session's search history
// newest-first plus any support messages the same session filed; unknown session
// ids 404 to avoid confirming their non-existence to a URL fuzzer.
[Collection(nameof(PostgresCollection))]
public sealed class SessionDrillInTests
{
    private readonly PostgresFixture _fixture;

    public SessionDrillInTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Session_KnownId_ReturnsViewModelWithEventsAndMessages()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        const string sessionId = "session-drill-1";

        await using (var seedCtx = _fixture.CreateContext())
        {
            seedCtx.SearchEvents.Add(NewEvent(sessionId, DateTime.UtcNow.AddMinutes(-30), "first"));
            seedCtx.SearchEvents.Add(NewEvent(sessionId, DateTime.UtcNow.AddMinutes(-10), "second"));
            seedCtx.SearchEvents.Add(NewEvent("other-session", DateTime.UtcNow.AddMinutes(-5), "not mine"));
            await seedCtx.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var query = new SearchAnalyticsQueryService(
                context,
                new StubSettingService(SettingKeys.SearchAnalyticsRetentionDays, 90));
        var messages = new DbSearchMessageService(context);
        await messages.CreateAsync(sessionId, "message from session-drill-1", null, null, CancellationToken.None);

        var controller = BuildController(context, query, messages);

        var result = await controller.Session(sessionId, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SearchSessionDrillInViewModel>(view.Model);
        Assert.Equal(sessionId, model.SessionId);
        Assert.Equal(2, model.Events.Count);
        // Newest-first ordering (the "second" event is newer than "first").
        Assert.Equal("second", model.Events[0].QueryRaw);
        Assert.Single(model.Messages);
    }

    [Fact]
    public async Task Session_UnknownId_Returns404()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        await using var context = _fixture.CreateContext();
        var query = new SearchAnalyticsQueryService(
                context,
                new StubSettingService(SettingKeys.SearchAnalyticsRetentionDays, 90));
        var messages = new DbSearchMessageService(context);

        var controller = BuildController(context, query, messages);

        var result = await controller.Session("no-such-session", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task SessionView_RendersHistoryTablePlusDangerZoneWithConfirmModal()
    {
        var now = DateTime.UtcNow;
        var model = new SearchSessionDrillInViewModel
        {
            SessionId = "session-render-abcdef0123",
            Events = new[]
            {
                new SessionHistoryRow(now, "widget", "widget", "pages", 1, 0, 1, 42),
            },
            Messages = new[]
            {
                new SearchMessageSummary(101, now, "session-render-abcdef0123", false, false, "message preview"),
            },
        };

        var html = await MessagesInboxTests.RenderViewAsync("/Views/Admin/Search/Session.cshtml", model);

        Assert.Contains("Session session-", html);
        Assert.Contains("session-render-abcdef0123", html);
        Assert.Contains("widget", html);
        Assert.Contains("Delete this session's data", html);
        // The govuk-confirm-modal tag helper emits a native <dialog> — assert on the
        // rendered id + destructive form action rather than the source tag name.
        Assert.Contains("id=\"session-purge-modal\"", html);
        Assert.Contains("<dialog", html);
        Assert.Contains("action=\"/admin/Search/Session/session-render-abcdef0123/Delete\"", html);
        // Related messages list links back to the messages inbox for each message id.
        Assert.Contains("/admin/Messages/Inbox/101", html);
    }

    // ---- Helpers ----

    private static SearchAnalyticsController BuildController(
        IPortalDbContext dbContext,
        ISearchAnalyticsQueryService query,
        ISearchMessageService messages)
    {
        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.CmsPageLength).Returns(20);

        var controller = new SearchAnalyticsController(
            query, settings, messages, dbContext, currentUserService: null);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext, Substitute.For<ITempDataProvider>());
        return controller;
    }

    private static SearchEvent NewEvent(string sessionId, DateTime occurredAtUtc, string queryText) => new()
    {
        OccurredAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc),
        SessionId = sessionId,
        QueryRaw = queryText,
        QueryNormalised = queryText,
        Scope = null,
        ResultsPages = 1,
        ResultsBlocks = 0,
        LatencyMs = 10,
    };
}
