using System.Reflection;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Controller-scope + view-render coverage of the message-detail page and the mark-read
// action. Detail includes the cross-link to /admin/Search/Session/{sessionId} that the
// support flow uses to jump from a message to the session that produced it.
[Collection(nameof(PostgresCollection))]
public sealed class MessagesDetailTests
{
    private readonly PostgresFixture _fixture;

    public MessagesDetailTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // --- Detail action ---

    [Fact]
    public async Task Detail_ExistingId_ReturnsViewWithMessageDetail()
    {
        await TruncateMessagesAsync();
        await using var context = _fixture.CreateContext();
        var messages = new DbSearchMessageService(context);
        var id = await messages.CreateAsync(
            "session-detail",
            "detailed body",
            "extra info",
            "user@example.com",
            CancellationToken.None);

        var controller = BuildController(messages, currentUserSub: "admin-sub");
        var result = await controller.Detail(id, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MessagesDetailViewModel>(view.Model);
        Assert.Equal(id, model.Message.Id);
        Assert.Equal("session-detail", model.Message.SessionId);
        Assert.Equal("/admin/Search/Session/session-detail", model.SessionDrillInHref);
    }

    [Fact]
    public async Task Detail_UnknownId_Returns404()
    {
        await TruncateMessagesAsync();
        await using var context = _fixture.CreateContext();
        var messages = new DbSearchMessageService(context);

        var controller = BuildController(messages, currentUserSub: "admin-sub");
        var result = await controller.Detail(9_999_999L, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    // --- Detail view render ---

    [Fact]
    public async Task DetailView_UnreadRow_RendersMarkReadFormAndSessionDrillInLink()
    {
        var model = new MessagesDetailViewModel
        {
            Message = new SearchMessageDetail(
                Id: 42,
                SubmittedAtUtc: DateTime.UtcNow,
                SessionId: "session-42",
                WhatLookingFor: "help me find the widget",
                WhatGot: "nothing",
                Email: "u@example.com",
                IsRead: false,
                ReadByAdminSub: null,
                ReadAtUtc: null),
        };

        var html = await MessagesInboxTests.RenderViewAsync("/Views/Admin/Messages/Detail.cshtml", model);

        Assert.Contains("help me find the widget", html);
        Assert.Contains("u@example.com", html);
        Assert.Contains("action=\"/admin/Messages/Inbox/42/MarkRead\"", html);
        Assert.Contains("Mark as read", html);
        Assert.Contains("href=\"/admin/Search/Session/session-42\"", html);
        Assert.Contains("View this session's searches", html);
    }

    [Fact]
    public async Task DetailView_NoEmail_RendersNoReplyAddressProvided()
    {
        var model = new MessagesDetailViewModel
        {
            Message = new SearchMessageDetail(
                Id: 1,
                SubmittedAtUtc: DateTime.UtcNow,
                SessionId: "s-empty-email",
                WhatLookingFor: "hidden email test",
                WhatGot: null,
                Email: null,
                IsRead: false,
                ReadByAdminSub: null,
                ReadAtUtc: null),
        };

        var html = await MessagesInboxTests.RenderViewAsync("/Views/Admin/Messages/Detail.cshtml", model);

        Assert.Contains("no email — user did not want to be contacted", html);
    }

    [Fact]
    public async Task DetailView_ReadRow_HidesMarkReadFormAndShowsAttribution()
    {
        var readAt = new DateTime(2026, 07, 27, 10, 30, 0, DateTimeKind.Utc);
        var model = new MessagesDetailViewModel
        {
            Message = new SearchMessageDetail(
                Id: 7,
                SubmittedAtUtc: DateTime.UtcNow,
                SessionId: "s-read",
                WhatLookingFor: "already read",
                WhatGot: null,
                Email: null,
                IsRead: true,
                ReadByAdminSub: "admin-sub-abc",
                ReadAtUtc: readAt),
        };

        var html = await MessagesInboxTests.RenderViewAsync("/Views/Admin/Messages/Detail.cshtml", model);

        Assert.DoesNotContain("MarkRead", html);
        Assert.Contains("admin-sub-abc", html);
        Assert.Contains("2026-07-27", html);
    }

    // --- Mark-read action ---

    [Fact]
    public async Task MarkRead_StampsAttributionAndRedirectsToDetail()
    {
        await TruncateMessagesAsync();
        await using var context = _fixture.CreateContext();
        var messages = new DbSearchMessageService(context);
        var id = await messages.CreateAsync("s-mark", "body", null, null, CancellationToken.None);

        var controller = BuildController(messages, currentUserSub: "admin-first-sub");
        var result = await controller.MarkRead(id, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(MessagesController.Detail), redirect.ActionName);
        Assert.Equal(id, redirect.RouteValues!["id"]);

        var detail = await messages.GetByIdAsync(id, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.True(detail!.IsRead);
        Assert.Equal("admin-first-sub", detail.ReadByAdminSub);
    }

    [Fact]
    public async Task MarkRead_SecondCall_DoesNotOverwriteFirstAdminAttribution()
    {
        await TruncateMessagesAsync();
        await using var context = _fixture.CreateContext();
        var messages = new DbSearchMessageService(context);
        var id = await messages.CreateAsync("s-mark", "body", null, null, CancellationToken.None);

        var firstController = BuildController(messages, currentUserSub: "admin-first-sub");
        await firstController.MarkRead(id, CancellationToken.None);

        var secondController = BuildController(messages, currentUserSub: "admin-second-sub");
        await secondController.MarkRead(id, CancellationToken.None);

        var detail = await messages.GetByIdAsync(id, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.True(detail!.IsRead);
        Assert.Equal("admin-first-sub", detail.ReadByAdminSub);
    }

    [Fact]
    public void MarkRead_HasValidateAntiForgeryTokenAttribute()
    {
        var method = typeof(MessagesController).GetMethod(nameof(MessagesController.MarkRead))!;
        var attrs = method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true);
        Assert.NotEmpty(attrs);
    }

    // --- Detail includes the user's prior-search snapshot on the view model ---

    [Fact]
    public async Task Detail_PopulatesPriorSearch_FromNewestEventAtOrBeforeSubmission()
    {
        await TruncateMessagesAsync();
        await TruncateSearchEventsAsync();

        await using var messagesContext = _fixture.CreateContext();
        var messages = new DbSearchMessageService(messagesContext);
        var id = await messages.CreateAsync(
            "session-with-history",
            "cannot find the widget page",
            null,
            null,
            CancellationToken.None);

        // Seed a search that happened BEFORE the message and one AFTER it. The detail view
        // must render the pre-submission search — a later one is not what the user was
        // looking at when they submitted the note.
        var submittedAt = (await messages.GetByIdAsync(id, CancellationToken.None))!.SubmittedAtUtc;
        var eventBefore = new SearchEvent
        {
            OccurredAtUtc = submittedAt.AddMinutes(-1),
            SessionId = "session-with-history",
            QueryRaw = "widget",
            QueryNormalised = "widget",
            ResultsPages = 2,
            ResultsBlocks = 0,
            LatencyMs = 12,
        };
        var eventAfter = new SearchEvent
        {
            OccurredAtUtc = submittedAt.AddMinutes(5),
            SessionId = "session-with-history",
            QueryRaw = "later query",
            QueryNormalised = "later query",
            ResultsPages = 4,
            ResultsBlocks = 0,
            LatencyMs = 20,
        };
        await using (var eventsContext = _fixture.CreateContext())
        {
            eventsContext.SearchEvents.AddRange(eventBefore, eventAfter);
            await eventsContext.SaveChangesAsync();
            eventsContext.SearchEventResults.AddRange(
                new SearchEventResult { SearchEventId = eventBefore.Id, Position = 1, ResultKind = "page",  ResultKey = "/help/widget-guide" },
                new SearchEventResult { SearchEventId = eventBefore.Id, Position = 2, ResultKind = "block", ResultKey = "home" });
            await eventsContext.SaveChangesAsync();
        }

        var queryService = new SearchAnalyticsQueryService(_fixture.CreateContext());
        var controller = BuildController(messages, queryService, currentUserSub: "admin-sub");

        var result = await controller.Detail(id, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MessagesDetailViewModel>(view.Model);
        Assert.NotNull(model.PriorSearch);
        Assert.Equal("widget", model.PriorSearch!.Query);
        Assert.Equal(2, model.PriorSearch.ResultsTotal);
        Assert.Equal(2, model.PriorSearch.Hits.Count);
        Assert.Equal("/help/widget-guide", model.PriorSearch.Hits[0].Key);
        Assert.Equal("home", model.PriorSearch.Hits[1].Key);
    }

    // --- Detail view renders the user's typed content + prior-search hits list ---

    [Fact]
    public async Task DetailView_WithPriorSearch_RendersHitsListWithNewTabLinks()
    {
        var model = new MessagesDetailViewModel
        {
            Message = new SearchMessageDetail(
                Id: 99,
                SubmittedAtUtc: new DateTime(2026, 07, 28, 10, 0, 0, DateTimeKind.Utc),
                SessionId: "session-99",
                WhatLookingFor: "I typed this exact question",
                WhatGot: null,
                Email: null,
                IsRead: false,
                ReadByAdminSub: null,
                ReadAtUtc: null),
            PriorSearch = new PriorSearchDisplay(
                Query: "the query the user ran",
                OccurredAtUtc: new DateTime(2026, 07, 28, 9, 59, 0, DateTimeKind.Utc),
                ResultsTotal: 2,
                Hits: new[]
                {
                    new PriorSearchHit(1, "page",  "/help/widget-guide"),
                    new PriorSearchHit(2, "block", "home"),
                }),
        };

        var html = await MessagesInboxTests.RenderViewAsync("/Views/Admin/Messages/Detail.cshtml", model);

        // The user's typed question is rendered verbatim.
        Assert.Contains("I typed this exact question", html);
        // Prior-search panel: query + count + numbered list.
        Assert.Contains("the query the user ran", html);
        Assert.Contains("govuk-list--number", html);
        // Every hit renders as a target=_blank link.
        Assert.Contains("href=\"/help/widget-guide\"", html);
        Assert.Contains("target=\"_blank\"", html);
        // The block key 'home' resolves to /.
        Assert.Contains("href=\"/\"", html);
    }

    // ---- Helpers ----

    private static MessagesController BuildController(
        ISearchMessageService messages,
        string currentUserSub,
        ISearchAnalyticsQueryService? query = null)
    {
        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.CmsPageLength).Returns(20);
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(currentUserSub);

        var controller = new MessagesController(messages, settings, currentUser, query);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext, Substitute.For<ITempDataProvider>());
        return controller;
    }

    private static MessagesController BuildController(
        ISearchMessageService messages,
        ISearchAnalyticsQueryService query,
        string currentUserSub) => BuildController(messages, currentUserSub, query);

    private async Task TruncateSearchEventsAsync()
    {
        await using var conn = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE search_events RESTART IDENTITY CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task TruncateMessagesAsync()
    {
        await using var conn = new Npgsql.NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE search_messages RESTART IDENTITY;";
        await cmd.ExecuteNonQueryAsync();
    }
}
