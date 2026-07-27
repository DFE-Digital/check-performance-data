using System.Net;
using System.Text.RegularExpressions;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Search;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Extensions;
using DfE.CheckPerformanceData.Web.Middleware;
using GovUk.Frontend.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Search;

// End-to-end coverage of the user-facing feedback surface. Two invariants carry the plan:
//
//   1. Server-side session id — every persisted row's session_id comes from
//      context.Session.Id read INSIDE the action, never from a client-supplied form field.
//      A malicious page or a hand-edited POST cannot bind a complaint to someone else's
//      session id by injecting a hidden field.
//   2. Hide-my-email drops the value before persist — no encryption, no reveal audit, no
//      IDataProtectionProvider machinery. When the checkbox is ticked the persisted
//      search_messages.email is literally NULL.
//
// The test host spins up a HostBuilder with the real session middleware pipeline (so
// Session.Id is materialised the same way production does) + real Postgres via the shared
// PostgresFixture. Antiforgery is disabled at the global filter level to keep the tests
// focused on the session-id + hide-email contracts; the production controller keeps
// [ValidateAntiForgeryToken] on Submit.
[Collection(nameof(PostgresCollection))]
public sealed class SearchFeedbackControllerTests
{
    private readonly PostgresFixture _fixture;

    public SearchFeedbackControllerTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // --- (a) GET renders readonly session-id input + POST ignores spoofed SessionIdDisplayOnly ---

    [Fact]
    public async Task Get_RendersReadonlySessionIdInput_AndPostIgnoresSpoofedSessionIdField()
    {
        await TruncateMessagesAsync();

        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        // First GET materialises the session cookie via SessionAbsoluteLifetimeMiddleware
        // and renders the form with the server's real session id in the readonly input.
        var getResp = await client.GetAsync("/Search/Feedback");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var getBody = await getResp.Content.ReadAsStringAsync();

        // The readonly session-id input MUST render on the page — this is the user-visible
        // display that lets the visitor quote the id to support. NOT a type=hidden field.
        var labelMatch = Regex.Match(
            getBody,
            @"<label[^>]*class=""[^""]*govuk-label[^""]*""[^>]*for=""SessionIdDisplayOnly""[^>]*>\s*Your session ID",
            RegexOptions.IgnoreCase);
        Assert.True(labelMatch.Success, "Expected a govuk-label 'Your session ID' associated with SessionIdDisplayOnly.");

        var inputMatch = Regex.Match(
            getBody,
            @"<input[^>]*id=""SessionIdDisplayOnly""[^>]*value=""([^""]+)""[^>]*readonly",
            RegexOptions.IgnoreCase);
        Assert.True(inputMatch.Success, "Expected a readonly govuk-input with id=SessionIdDisplayOnly.");
        var serverSessionId = inputMatch.Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(serverSessionId));

        var sessionCookie = ExtractSessionCookie(getResp);

        // POST with a SPOOFED SessionIdDisplayOnly value that has no relation to the
        // server's session cookie. The controller must ignore the form field and stamp
        // context.Session.Id (i.e. serverSessionId) onto the row.
        var post = await PostAsync(client, "/Search/Feedback", sessionCookie, new Dictionary<string, string>
        {
            ["SessionIdDisplayOnly"] = "SPOOFED_SESSION_VALUE",
            ["WhatLookingFor"] = "does the form ignore my hidden field?",
        });

        Assert.True(post.StatusCode == HttpStatusCode.Redirect
                    || post.StatusCode == HttpStatusCode.Found
                    || post.StatusCode == HttpStatusCode.SeeOther,
            $"Expected a redirect after a successful POST but got {(int)post.StatusCode}.");

        var row = await SelectSingleMessageAsync();
        Assert.Equal(serverSessionId, row.SessionId);
        Assert.NotEqual("SPOOFED_SESSION_VALUE", row.SessionId);
    }

    // --- (b) HideMyEmail ticked drops the email value before persist ---

    [Fact]
    public async Task Post_HideMyEmailTrue_PersistsEmailAsNull()
    {
        await TruncateMessagesAsync();

        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        var getResp = await client.GetAsync("/Search/Feedback");
        var sessionCookie = ExtractSessionCookie(getResp);

        var post = await PostAsync(client, "/Search/Feedback", sessionCookie, new Dictionary<string, string>
        {
            ["WhatLookingFor"] = "I ticked the hide box",
            ["Email"] = "user@example.gov.uk",
            ["HideMyEmail"] = "true",
        });

        Assert.True((int)post.StatusCode is 301 or 302 or 303,
            $"Expected a redirect but got {(int)post.StatusCode}.");

        var row = await SelectSingleMessageAsync();
        Assert.Null(row.Email);
    }

    // --- (c) HideMyEmail unticked with email present stores it verbatim ---

    [Fact]
    public async Task Post_HideMyEmailFalse_WithEmail_PersistsEmailVerbatim()
    {
        await TruncateMessagesAsync();

        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        var getResp = await client.GetAsync("/Search/Feedback");
        var sessionCookie = ExtractSessionCookie(getResp);

        var post = await PostAsync(client, "/Search/Feedback", sessionCookie, new Dictionary<string, string>
        {
            ["WhatLookingFor"] = "please reply to me",
            ["Email"] = "user@example.gov.uk",
            // HideMyEmail deliberately omitted — the checkbox unticked posts nothing for
            // the field, and the bool model-binds to false.
        });

        Assert.True((int)post.StatusCode is 301 or 302 or 303);

        var row = await SelectSingleMessageAsync();
        Assert.Equal("user@example.gov.uk", row.Email);
    }

    // --- (d) Empty WhatLookingFor renders the form with a validation error and persists nothing ---

    [Fact]
    public async Task Post_EmptyWhatLookingFor_ReturnsValidationErrorAndDoesNotPersist()
    {
        await TruncateMessagesAsync();

        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        var getResp = await client.GetAsync("/Search/Feedback");
        var sessionCookie = ExtractSessionCookie(getResp);

        var post = await PostAsync(client, "/Search/Feedback", sessionCookie, new Dictionary<string, string>
        {
            ["WhatLookingFor"] = string.Empty,
        });

        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        var body = await post.Content.ReadAsStringAsync();
        Assert.Contains("govuk-error-message", body);

        Assert.Equal(0, await CountMessagesAsync());
    }

    // --- (e) When the session has a prior search event, WhatGot is pre-filled ---

    [Fact]
    public async Task Get_WhenSessionHasPriorSearch_PrefillsWhatGotTextarea()
    {
        await TruncateAllAsync();

        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        // First GET materialises the session id.
        var firstResp = await client.GetAsync("/Search/Feedback");
        var firstBody = await firstResp.Content.ReadAsStringAsync();
        var sessionCookie = ExtractSessionCookie(firstResp);
        var serverSessionId = ExtractServerSessionId(firstBody);

        // Seed a search_events row keyed by that session id so the second GET's pre-fill
        // helper picks it up.
        await using (var seedContext = _fixture.CreateContext())
        {
            seedContext.SearchEvents.Add(new SearchEvent
            {
                OccurredAtUtc = DateTime.UtcNow.AddMinutes(-2),
                SessionId = serverSessionId,
                QueryRaw = "widget",
                QueryNormalised = "widget",
                Scope = null,
                ResultsPages = 4,
                ResultsBlocks = 0,
                LatencyMs = 15,
            });
            await seedContext.SaveChangesAsync();
        }

        // Second GET on the same cookie must render the pre-fill inside the WhatGot textarea.
        var secondReq = new HttpRequestMessage(HttpMethod.Get, "/Search/Feedback");
        secondReq.Headers.Add("Cookie", sessionCookie);
        var secondResp = await client.SendAsync(secondReq);
        var secondBody = await secondResp.Content.ReadAsStringAsync();

        // The pre-fill text is written INSIDE the textarea element (textareas don't use
        // value= — their content goes between the open + close tags). Assert the query +
        // result count are both present in the textarea for WhatGot.
        var textareaMatch = Regex.Match(
            secondBody,
            @"<textarea[^>]*name=""WhatGot""[^>]*>([^<]*)</textarea>",
            RegexOptions.IgnoreCase);
        Assert.True(textareaMatch.Success, "Expected a textarea named WhatGot.");
        var prefillContent = textareaMatch.Groups[1].Value;
        Assert.Contains("widget", prefillContent);
        Assert.Contains("4", prefillContent);
    }

    // --- (f) A successful POST redirects to a confirmation view that shows the session id ---

    [Fact]
    public async Task Post_Success_RedirectsToConfirmationViewShowingSessionId()
    {
        await TruncateMessagesAsync();

        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        var getResp = await client.GetAsync("/Search/Feedback");
        var getBody = await getResp.Content.ReadAsStringAsync();
        var sessionCookie = ExtractSessionCookie(getResp);
        var serverSessionId = ExtractServerSessionId(getBody);

        var post = await PostAsync(client, "/Search/Feedback", sessionCookie, new Dictionary<string, string>
        {
            ["WhatLookingFor"] = "confirmation redirect test",
        });

        Assert.True((int)post.StatusCode is 301 or 302 or 303);
        var location = post.Headers.Location?.ToString();
        Assert.False(string.IsNullOrEmpty(location), "Successful POST must set a Location header.");

        // Follow the redirect on the same cookie so TempData (if used) survives.
        var followReq = new HttpRequestMessage(HttpMethod.Get, location);
        followReq.Headers.Add("Cookie", sessionCookie);
        var confirmationResp = await client.SendAsync(followReq);
        Assert.Equal(HttpStatusCode.OK, confirmationResp.StatusCode);
        var confirmationBody = await confirmationResp.Content.ReadAsStringAsync();
        Assert.Contains(serverSessionId, confirmationBody);
    }

    // -----------------------------------------------------------------
    // Test host + helper plumbing
    // -----------------------------------------------------------------

    private async Task<IHost> BuildHostAsync()
    {
        var connectionString = _fixture.ConnectionString;
        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["SearchAnalytics:SessionAbsoluteHours"] = "24",
                        ["SearchAnalytics:SessionIdleMinutes"] = "60",
                    });
                });
                web.ConfigureServices((ctx, services) =>
                {
                    services.AddHttpContextAccessor();

                    // Session backing store: in-memory so the test is self-contained (production
                    // uses Postgres via AddDistributedPostgreSqlCache; the session behaviour is
                    // identical for what the test exercises).
                    services.AddDistributedMemoryCache();
                    services.AddCpdSession(ctx.Configuration);

                    // Real DbContext against the shared Postgres fixture so the message-service
                    // insert lands in the same table the assertions read from.
                    services.AddDbContext<PortalDbContext>(o => o.UseNpgsql(connectionString));
                    services.AddScoped<IPortalDbContext>(sp => sp.GetRequiredService<PortalDbContext>());
                    services.AddScoped<ISearchMessageService, DbSearchMessageService>();
                    services.AddScoped<ISearchAnalyticsQueryService, SearchAnalyticsQueryService>();

                    // GDS tag helpers used inside Feedback.cshtml.
                    services.AddGovUkFrontend();

                    // Antiforgery: register the services so [ValidateAntiForgeryToken] resolves,
                    // and disable the token check globally via IgnoreAntiforgeryTokenAttribute so
                    // the test's raw HTTP POSTs (no browser cookie container, no token round-trip)
                    // still reach the action. Production controller keeps [ValidateAntiForgeryToken];
                    // this override lives only inside the test host.
                    services.AddAntiforgery();
                    services.AddControllersWithViews(o =>
                        o.Filters.Add<IgnoreAntiforgeryTokenAttribute>())
                        .AddApplicationPart(typeof(SearchFeedbackController).Assembly);
                });
                web.Configure(app =>
                {
                    app.UseSession();
                    // Absolute-lifetime middleware ALSO commits the session cookie on first
                    // access via SetString — without it, Session.Id would not materialise and
                    // the controller would see a fresh id on every request.
                    app.UseMiddleware<SessionAbsoluteLifetimeMiddleware>();
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            })
            .StartAsync();
        return host;
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string url, string sessionCookie, IDictionary<string, string> fields)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(fields),
        };
        req.Headers.Add("Cookie", sessionCookie);
        return await client.SendAsync(req);
    }

    private static string ExtractSessionCookie(HttpResponseMessage response)
    {
        if (!response.Headers.Contains("Set-Cookie"))
            throw new Xunit.Sdk.XunitException("Response did not carry a Set-Cookie header.");
        foreach (var raw in response.Headers.GetValues("Set-Cookie"))
        {
            if (raw.StartsWith(".AspNetCore.Session=", StringComparison.Ordinal))
            {
                var end = raw.IndexOf(';');
                return end < 0 ? raw : raw[..end];
            }
        }
        throw new Xunit.Sdk.XunitException("No .AspNetCore.Session cookie in response.");
    }

    private static string ExtractServerSessionId(string html)
    {
        var match = Regex.Match(
            html,
            @"<input[^>]*id=""SessionIdDisplayOnly""[^>]*value=""([^""]+)""",
            RegexOptions.IgnoreCase);
        if (!match.Success)
            throw new Xunit.Sdk.XunitException("Could not locate the SessionIdDisplayOnly input value in the rendered form.");
        return match.Groups[1].Value;
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

// View-render coverage of the "Not the results you were expecting?" inset-text link on
// Views/Search/Index.cshtml. Renders the view standalone through the composite view engine
// (isMainPage:false → skips _ViewStart / _Layout) so the tests do not need the full
// public-facing layout dependency graph. Mirrors SearchAnalyticsIndexViewRenderTests.
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
                    // NSubstitute fake returning ShowSearchDebug=false so the debug-only
                    // markup branches don't fire in the assertion body.
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
