using System.Text.RegularExpressions;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using GovUk.Frontend.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Search;

// Controller-scope invocation of SearchFeedbackController against a real Postgres
// DbContext + real DbSearchMessageService. Full HTTP-through-layout invocation is
// avoided because Views/_ViewStart.cshtml → _Layout.cshtml → _GovUkPageTemplate pulls
// in the whole content-blocks + admin-nav dependency graph (EditableContentViewComponent
// etc.) — same reason SearchAnalyticsAuthTests split HTTP auth checks from
// controller-scope model + view-render assertions in P03. Direct action invocation
// proves the two invariants the plan cares about most:
//   1. context.Session.Id — NOT any client-supplied field — is what lands in
//      search_messages.session_id
//   2. HideMyEmail ticked drops the email BEFORE persist so the DB row's email column
//      is literally NULL
// The Feedback + FeedbackConfirmation view templates are covered by
// SearchFeedbackViewRenderTests below; the inset-text link on /search Index.cshtml is
// covered by SearchIndexInsetTextRenderTests at the bottom of this file.
[Collection(nameof(PostgresCollection))]
public sealed class SearchFeedbackControllerTests
{
    private readonly PostgresFixture _fixture;

    public SearchFeedbackControllerTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // --- (a) Server-side session id wins over any client-supplied form field ---

    [Fact]
    public async Task Submit_IgnoresClientSuppliedSessionIdField_UsesHttpContextSessionId()
    {
        await TruncateMessagesAsync();

        const string serverSessionId = "server-owned-session-id-12345";
        await using var context = _fixture.CreateContext();
        var messages = new DbSearchMessageService(context);
        var query = Substitute.For<ISearchAnalyticsQueryService>();

        var controller = BuildController(messages, query, sessionId: serverSessionId);

        // The form-posted view model carries a spoofed SessionId — the property IS on
        // the view model, but the controller re-reads HttpContext.Session.Id after
        // model binding runs, so the spoofed value never reaches the message service.
        var form = new SearchFeedbackViewModel
        {
            SessionId = "SPOOFED-SESSION-VALUE",
            WhatLookingFor = "does the controller ignore the spoofed session id?",
        };

        var result = await controller.Submit(form, CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);

        var row = await SelectSingleMessageAsync();
        Assert.Equal(serverSessionId, row.SessionId);
        Assert.NotEqual("SPOOFED-SESSION-VALUE", row.SessionId);
    }

    // --- (b) HideMyEmail ticked drops the email value BEFORE persist ---

    [Fact]
    public async Task Submit_HideMyEmailTrue_PersistsEmailAsNull()
    {
        await TruncateMessagesAsync();

        await using var context = _fixture.CreateContext();
        var messages = new DbSearchMessageService(context);
        var query = Substitute.For<ISearchAnalyticsQueryService>();

        var controller = BuildController(messages, query, sessionId: "session-hide");

        var form = new SearchFeedbackViewModel
        {
            WhatLookingFor = "I ticked the hide box",
            Email = "user@example.gov.uk",
            HideMyEmail = true,
        };

        var result = await controller.Submit(form, CancellationToken.None);
        Assert.IsType<RedirectToActionResult>(result);

        var row = await SelectSingleMessageAsync();
        Assert.Null(row.Email);
    }

    // --- (c) HideMyEmail unticked with email present stores it verbatim ---

    [Fact]
    public async Task Submit_HideMyEmailFalse_WithEmail_PersistsEmailVerbatim()
    {
        await TruncateMessagesAsync();

        await using var context = _fixture.CreateContext();
        var messages = new DbSearchMessageService(context);
        var query = Substitute.For<ISearchAnalyticsQueryService>();

        var controller = BuildController(messages, query, sessionId: "session-with-email");

        var form = new SearchFeedbackViewModel
        {
            WhatLookingFor = "please reply to me",
            Email = "user@example.gov.uk",
            HideMyEmail = false,
        };

        var result = await controller.Submit(form, CancellationToken.None);
        Assert.IsType<RedirectToActionResult>(result);

        var row = await SelectSingleMessageAsync();
        Assert.Equal("user@example.gov.uk", row.Email);
    }

    // --- (d) Empty WhatLookingFor renders the form with a validation error and persists nothing ---

    [Fact]
    public async Task Submit_EmptyWhatLookingFor_ReturnsViewWithInvalidModelStateAndDoesNotPersist()
    {
        await TruncateMessagesAsync();

        await using var context = _fixture.CreateContext();
        var messages = new DbSearchMessageService(context);
        var query = Substitute.For<ISearchAnalyticsQueryService>();

        var controller = BuildController(messages, query, sessionId: "session-validation");

        // Emulate the framework's model-validation pass — a real POST runs the [Required]
        // attribute on WhatLookingFor via IObjectModelValidator; the unit-invoked action
        // sees whatever ModelState the caller populates. Set the error explicitly so the
        // controller's ModelState.IsValid check exercises the redisplay branch.
        var form = new SearchFeedbackViewModel { WhatLookingFor = string.Empty };
        controller.ModelState.AddModelError(
            nameof(SearchFeedbackViewModel.WhatLookingFor),
            "Enter what you were looking for");

        var result = await controller.Submit(form, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Search/Feedback.cshtml", view.ViewName);
        Assert.False(controller.ModelState.IsValid);
        Assert.True(controller.ModelState.ContainsKey(nameof(SearchFeedbackViewModel.WhatLookingFor)));
        Assert.Equal(0, await CountMessagesAsync());
    }

    // --- (e) When the session has a prior search event, the view model's PriorSearchPrefill is formatted ---

    [Fact]
    public async Task Index_WhenSessionHasPriorSearch_SetsPriorSearchPrefillFromLatestEvent()
    {
        await TruncateAllAsync();

        const string sessionId = "session-with-history";

        await using (var seedContext = _fixture.CreateContext())
        {
            seedContext.SearchEvents.Add(new SearchEvent
            {
                OccurredAtUtc = DateTime.SpecifyKind(new DateTime(2026, 8, 3, 14, 27, 0), DateTimeKind.Utc),
                SessionId = sessionId,
                QueryRaw = "widget",
                QueryNormalised = "widget",
                Scope = null,
                ResultsPages = 4,
                ResultsBlocks = 0,
                LatencyMs = 15,
            });
            await seedContext.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var messages = new DbSearchMessageService(context);
        var query = new SearchAnalyticsQueryService(context);

        var controller = BuildController(messages, query, sessionId: sessionId);

        var result = await controller.Index(CancellationToken.None);
        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SearchFeedbackViewModel>(view.Model);

        Assert.Equal(sessionId, model.SessionId);
        Assert.NotNull(model.PriorSearchPrefill);
        Assert.Contains("widget", model.PriorSearchPrefill);
        Assert.Contains("4", model.PriorSearchPrefill);
    }

    [Fact]
    public async Task Index_WhenSessionHasNoPriorSearch_LeavesPriorSearchPrefillNull()
    {
        await TruncateAllAsync();

        await using var context = _fixture.CreateContext();
        var messages = new DbSearchMessageService(context);
        var query = new SearchAnalyticsQueryService(context);

        var controller = BuildController(messages, query, sessionId: "session-with-no-history");

        var result = await controller.Index(CancellationToken.None);
        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SearchFeedbackViewModel>(view.Model);

        Assert.Null(model.PriorSearchPrefill);
        Assert.Null(model.PriorSearch);
    }

    // --- (e2) When the session has a prior search event, the view model's PriorSearch panel
    //         carries the query, timestamp, results total, and the individual result rows in
    //         rendered order (regression against showing "N results" without the list) ---

    [Fact]
    public async Task Index_WhenSessionHasPriorSearchWithHits_PopulatesPriorSearchWithRenderedResultRows()
    {
        await TruncateAllAsync();

        const string sessionId = "session-with-hits";

        long eventId;
        await using (var seedContext = _fixture.CreateContext())
        {
            var evt = new SearchEvent
            {
                OccurredAtUtc = DateTime.SpecifyKind(new DateTime(2026, 8, 4, 09, 15, 0), DateTimeKind.Utc),
                SessionId = sessionId,
                QueryRaw = "widget",
                QueryNormalised = "widget",
                Scope = null,
                ResultsPages = 2,
                ResultsBlocks = 1,
                LatencyMs = 22,
            };
            seedContext.SearchEvents.Add(evt);
            await seedContext.SaveChangesAsync();
            eventId = evt.Id;

            seedContext.SearchEventResults.AddRange(
                new SearchEventResult { SearchEventId = eventId, Position = 1, ResultKind = "page",  ResultKey = "/help/widgets-overview", Rank = 1.0f },
                new SearchEventResult { SearchEventId = eventId, Position = 2, ResultKind = "page",  ResultKey = "/help/widgets-setup",    Rank = 0.8f },
                new SearchEventResult { SearchEventId = eventId, Position = 3, ResultKind = "block", ResultKey = "block-widget-intro",     Rank = 0.4f });
            await seedContext.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var messages = new DbSearchMessageService(context);
        var query = new SearchAnalyticsQueryService(context);

        var controller = BuildController(messages, query, sessionId: sessionId);

        var result = await controller.Index(CancellationToken.None);
        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<SearchFeedbackViewModel>(view.Model);

        Assert.NotNull(model.PriorSearch);
        Assert.Equal("widget", model.PriorSearch!.Query);
        Assert.Equal(3, model.PriorSearch.ResultsTotal);
        Assert.Equal(3, model.PriorSearch.Hits.Count);
        Assert.Equal(1, model.PriorSearch.Hits[0].Position);
        Assert.Equal("/help/widgets-overview", model.PriorSearch.Hits[0].Key);
        Assert.Equal("page", model.PriorSearch.Hits[0].Kind);
        Assert.Equal("/help/widgets-setup", model.PriorSearch.Hits[1].Key);
        Assert.Equal("block-widget-intro", model.PriorSearch.Hits[2].Key);
        Assert.Equal("block", model.PriorSearch.Hits[2].Kind);
    }

    // --- (f) Successful POST redirects to Confirmation and carries the session id via TempData ---

    [Fact]
    public async Task Submit_Success_RedirectsToConfirmationCarryingSessionIdInTempData()
    {
        await TruncateMessagesAsync();

        const string sessionId = "session-confirmation";
        await using var context = _fixture.CreateContext();
        var messages = new DbSearchMessageService(context);
        var query = Substitute.For<ISearchAnalyticsQueryService>();

        var controller = BuildController(messages, query, sessionId: sessionId);

        var form = new SearchFeedbackViewModel
        {
            WhatLookingFor = "confirmation redirect test",
        };

        var result = await controller.Submit(form, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SearchFeedbackController.Confirmation), redirect.ActionName);
        Assert.Equal(sessionId, controller.TempData["FeedbackSessionId"]);
    }

    [Fact]
    public void Confirmation_ReadsSessionIdFromTempDataOntoTheViewModel()
    {
        const string sessionId = "session-shown-on-confirmation";
        var messages = Substitute.For<ISearchMessageService>();
        var query = Substitute.For<ISearchAnalyticsQueryService>();

        var controller = BuildController(messages, query, sessionId: "some-other-current-session");
        controller.TempData["FeedbackSessionId"] = sessionId;

        var result = controller.Confirmation();

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("~/Views/Search/FeedbackConfirmation.cshtml", view.ViewName);
        var model = Assert.IsType<SearchFeedbackViewModel>(view.Model);
        Assert.Equal(sessionId, model.SessionId);
    }

    // --- Attribute assertions: [AllowAnonymous] + route wiring + antiforgery on Submit ---

    [Fact]
    public void Controller_IsAllowAnonymous()
    {
        var attrs = typeof(SearchFeedbackController)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), inherit: true);
        Assert.NotEmpty(attrs);
    }

    [Fact]
    public void Submit_HasValidateAntiForgeryTokenAttribute()
    {
        var submit = typeof(SearchFeedbackController).GetMethod(nameof(SearchFeedbackController.Submit))!;
        var attrs = submit.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true);
        Assert.NotEmpty(attrs);
    }

    // -----------------------------------------------------------------
    // Helper plumbing
    // -----------------------------------------------------------------

    private static SearchFeedbackController BuildController(
        ISearchMessageService messages,
        ISearchAnalyticsQueryService query,
        string sessionId)
    {
        var controller = new SearchFeedbackController(messages, query);
        var httpContext = BuildHttpContextWithSession(sessionId);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Substitute.For<ITempDataProvider>());
        return controller;
    }

    private static DefaultHttpContext BuildHttpContextWithSession(string sessionId)
    {
        var ctx = new DefaultHttpContext();
        ctx.Features.Set<ISessionFeature>(new FakeSessionFeature(new FakeSession(sessionId)));
        return ctx;
    }

    private sealed class FakeSessionFeature : ISessionFeature
    {
        public FakeSessionFeature(ISession session) { Session = session; }
        public ISession Session { get; set; }
    }

    // Minimal ISession implementation that returns a fixed Id — enough for the
    // controller to read HttpContext.Session.Id and for LoadAsync() to be a no-op.
    private sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public FakeSession(string id) { Id = id; }
        public bool IsAvailable => true;
        public string Id { get; }
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

    private async Task TruncateMessagesAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE search_messages RESTART IDENTITY;";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task TruncateAllAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE search_events, search_messages RESTART IDENTITY CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> CountMessagesAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*)::int FROM search_messages;";
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<PersistedRow> SelectSingleMessageAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT session_id, what_looking_for, what_got, email, is_read
            FROM search_messages
            ORDER BY id ASC
            LIMIT 1;";
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Expected exactly one row in search_messages.");
        return new PersistedRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetBoolean(4));
    }

    private sealed record PersistedRow(
        string SessionId, string WhatLookingFor, string? WhatGot, string? Email, bool IsRead);
}

// View-render coverage of Views/Search/Feedback.cshtml + Views/Search/FeedbackConfirmation.cshtml.
// Renders standalone through the composite view engine (isMainPage:false) so the tests
// don't need to spin up _Layout / _GovUkPageTemplate + their whole dependency graph.
// Mirrors SearchAnalyticsIndexViewRenderTests.
public sealed class SearchFeedbackViewRenderTests
{
    [Fact]
    public async Task Feedback_RendersReadonlySessionIdInputWithGovukLabel()
    {
        var model = new SearchFeedbackViewModel { SessionId = "abc-def-ghi", PriorSearchPrefill = null };

        var html = await RenderAsync("/Views/Search/Feedback.cshtml", model);

        Assert.Contains("Your session ID", html);
        // Readonly govuk-input carrying the session id — user-visible display, NOT a
        // hidden field.
        Assert.Matches(new Regex(@"<label[^>]*for=""SessionIdDisplayOnly""", RegexOptions.IgnoreCase), html);
        Assert.Matches(
            new Regex(@"<input[^>]*id=""SessionIdDisplayOnly""[^>]*value=""abc-def-ghi""[^>]*readonly", RegexOptions.IgnoreCase),
            html);
        // No hidden variant of the session id smuggled in.
        Assert.DoesNotMatch(
            new Regex(@"type=""hidden""[^>]*name=""SessionId""|name=""SessionId""[^>]*type=""hidden""", RegexOptions.IgnoreCase),
            html);
    }

    [Fact]
    public async Task Feedback_RendersAllSixFieldsPerSpec()
    {
        var model = new SearchFeedbackViewModel { SessionId = "sid-1" };

        var html = await RenderAsync("/Views/Search/Feedback.cshtml", model);

        Assert.Contains("SessionIdDisplayOnly", html);
        Assert.Contains("name=\"WhatLookingFor\"", html);
        Assert.Contains("name=\"WhatGot\"", html);
        Assert.Contains("name=\"Email\"", html);
        Assert.Contains("name=\"HideMyEmail\"", html);
        // Send button submits the form.
        Assert.Contains("Send", html);
        // Verbatim consent copy from the design spec.
        Assert.Contains(
            "If you provide your email address, it will be sent to the support team so they can reply to you.",
            html);
        // Hide-my-email checkbox label.
        Assert.Contains("Hide my email address from the support team", html);
    }

    [Fact]
    public async Task Feedback_PriorSearchPrefill_LandsInsideWhatGotTextarea()
    {
        var model = new SearchFeedbackViewModel
        {
            SessionId = "sid-2",
            PriorSearchPrefill = "Search: \"widget\" returned 4 results at 14:27 on 3 Aug 2026",
        };

        var html = await RenderAsync("/Views/Search/Feedback.cshtml", model);

        var textareaMatch = Regex.Match(
            html,
            @"<textarea[^>]*name=""WhatGot""[^>]*>([^<]*)</textarea>",
            RegexOptions.IgnoreCase);
        Assert.True(textareaMatch.Success, "Expected a textarea named WhatGot.");
        var content = textareaMatch.Groups[1].Value;
        Assert.Contains("widget", content);
        Assert.Contains("4 results", content);
    }

    [Fact]
    public async Task Feedback_ShowsErrorSummary_WhenModelStateIsInvalid()
    {
        var model = new SearchFeedbackViewModel { SessionId = "sid-err" };
        var modelState = new ModelStateDictionary();
        modelState.AddModelError(nameof(SearchFeedbackViewModel.WhatLookingFor), "Enter what you were looking for");

        var html = await RenderAsync("/Views/Search/Feedback.cshtml", model, modelState);

        Assert.Contains("govuk-error-summary", html);
        Assert.Contains("Enter what you were looking for", html);
        Assert.Contains("govuk-form-group--error", html);
    }

    [Fact]
    public async Task FeedbackConfirmation_ShowsSessionIdInBody()
    {
        var model = new SearchFeedbackViewModel { SessionId = "session-on-confirmation" };

        var html = await RenderAsync("/Views/Search/FeedbackConfirmation.cshtml", model);

        Assert.Contains("session-on-confirmation", html);
        Assert.Contains("Thanks", html);
    }

    private static async Task<string> RenderAsync(
        string viewPath,
        SearchFeedbackViewModel model,
        ModelStateDictionary? modelState = null)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddControllersWithViews()
                        .AddApplicationPart(typeof(SearchFeedbackController).Assembly);
                    services.AddGovUkFrontend();
                });
                web.Configure(_ => { });
            })
            .StartAsync();

        using var scope = host.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var viewEngine = sp.GetRequiredService<ICompositeViewEngine>();
        var tempDataProvider = sp.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var routeData = new RouteData();
        routeData.Values["controller"] = "SearchFeedback";
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        var view = viewEngine.GetView(executingFilePath: null, viewPath: viewPath, isMainPage: false);
        Assert.True(view.Success, $"Could not locate {viewPath}. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var viewData = new ViewDataDictionary<SearchFeedbackViewModel>(
            new EmptyModelMetadataProvider(), modelState ?? new ModelStateDictionary())
        {
            Model = model,
        };
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);

        await using var writer = new StringWriter();
        var viewContext = new ViewContext(
            actionContext, view.View, viewData, tempData, writer, new HtmlHelperOptions());
        await view.View.RenderAsync(viewContext);

        return writer.ToString();
    }
}

// View-render coverage of the "Not the results you were expecting?" inset-text link on
// Views/Search/Index.cshtml. Renders the view standalone through the composite view
// engine so no full public-facing layout is needed.
public sealed class SearchIndexInsetTextRenderTests
{
    [Fact]
    public async Task NoQueryPresent_OmitsInsetTextLink()
    {
        var model = BuildModel(query: string.Empty, hits: Array.Empty<CanonicalSearchHit>(),
            invalidReason: SearchInvalidReason.EmptyQuery, totalCount: 0);

        var html = await RenderIndexAsync(model);

        // With no query the inset-text callout must not render — the user hasn't yet had
        // a search experience to complain about.
        Assert.DoesNotContain("govuk-inset-text", html);
        Assert.DoesNotContain("/Search/Feedback", html);
    }

    [Fact]
    public async Task ResultsPresent_RendersInsetTextLinkToFeedbackForm()
    {
        var model = BuildModel(query: "widget", hits: new[]
        {
            new CanonicalSearchHit("/pages/widget", "Widget", "<p>match</p>", 0.5f, 1, 0, Array.Empty<string>()),
        }, invalidReason: null, totalCount: 1);

        var html = await RenderIndexAsync(model);

        Assert.Contains("govuk-inset-text", html);
        Assert.Contains("/Search/Feedback", html);
        Assert.Contains("Not the results you were expecting", html);
    }

    [Fact]
    public async Task ZeroResults_RendersInsetTextLinkToFeedbackForm()
    {
        var model = BuildModel(query: "xyzzynoresult", hits: Array.Empty<CanonicalSearchHit>(),
            invalidReason: null, totalCount: 0);

        var html = await RenderIndexAsync(model);

        Assert.Contains("govuk-inset-text", html);
        Assert.Contains("/Search/Feedback", html);
        Assert.Contains("Not the results you were expecting", html);
    }

    private static SiteSearchViewModel BuildModel(
        string query,
        IReadOnlyList<CanonicalSearchHit> hits,
        SearchInvalidReason? invalidReason,
        int totalCount) => new()
    {
        Query = query,
        Scope = null,
        InvalidReason = invalidReason,
        Hits = hits,
        IncludePages = true,
        IncludeContentBlocks = true,
        Page = 1,
        PageSize = 20,
        TotalCount = totalCount,
    };

    private static async Task<string> RenderIndexAsync(SiteSearchViewModel model)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddControllersWithViews()
                        .AddApplicationPart(typeof(SearchController).Assembly);
                    services.AddGovUkFrontend();
                    // Views/Search/Index.cshtml @inject-s ISearchDebugOptions; register a
                    // fake returning ShowSearchDebug=false so the debug-only markup
                    // branches don't fire in the assertion body.
                    var debugOptions = Substitute.For<ISearchDebugOptions>();
                    debugOptions.ShowSearchDebug.Returns(false);
                    services.AddSingleton(debugOptions);
                });
                web.Configure(_ => { });
            })
            .StartAsync();

        using var scope = host.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var viewEngine = sp.GetRequiredService<ICompositeViewEngine>();
        var tempDataProvider = sp.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var routeData = new RouteData();
        routeData.Values["controller"] = "Search";
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        var view = viewEngine.GetView(executingFilePath: null, viewPath: "/Views/Search/Index.cshtml", isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate Search Index view. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var viewData = new ViewDataDictionary<SiteSearchViewModel>(
            new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model,
        };
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);

        await using var writer = new StringWriter();
        var viewContext = new ViewContext(
            actionContext, view.View, viewData, tempData, writer, new HtmlHelperOptions());
        await view.View.RenderAsync(viewContext);

        return writer.ToString();
    }
}
