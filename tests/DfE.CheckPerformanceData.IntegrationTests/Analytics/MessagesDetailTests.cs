using System.Reflection;
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

        Assert.Contains("no reply address provided", html);
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

    // ---- Helpers ----

    private static MessagesController BuildController(ISearchMessageService messages, string currentUserSub)
    {
        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.CmsPageLength).Returns(20);
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(currentUserSub);

        var controller = new MessagesController(messages, settings, currentUser);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext, Substitute.For<ITempDataProvider>());
        return controller;
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
