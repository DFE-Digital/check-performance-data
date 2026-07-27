using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DfE.CheckPerformanceData.Application.Admin;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using GovUk.Frontend.AspNetCore;
using Microsoft.AspNetCore.Authentication;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Net;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Controller-scope + view-render coverage of the admin messages inbox list. Follows the
// P03/P04/P05 pattern of splitting HTTP-level auth checks (which need a HostBuilder +
// TestServer) from controller-scope model checks (direct action invocation against a
// real DbContext) — the full pipeline with the admin layout drags in the content-blocks
// dep graph. Auth is asserted at HTTP level here; sort / filter / paginate + view render
// are asserted at direct-invocation level.
[Collection(nameof(PostgresCollection))]
public sealed class MessagesInboxTests
{
    private readonly PostgresFixture _fixture;

    public MessagesInboxTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // --- Attribute gate is wired at the class level with the correct section key ---

    [Fact]
    public void Controller_IsGatedBy_MessagesInboxSection()
    {
        var gate = typeof(MessagesController).GetCustomAttribute<RequireAdminSectionAttribute>();
        Assert.NotNull(gate);
        Assert.Equal(AdminNavKeys.MessagesInbox, gate!.SectionKey);
    }

    [Fact]
    public void Controller_HasClassLevelRoute_AdminMessages()
    {
        var route = typeof(MessagesController).GetCustomAttribute<RouteAttribute>();
        Assert.NotNull(route);
        Assert.Equal("admin/Messages", route!.Template);
    }

    // --- HTTP-level auth branches ---

    [Fact]
    public async Task InboxHttp_AnonymousRequest_RedirectsToSignIn()
    {
        using var host = await BuildHostAsync(TestPrincipal.Anonymous);
        var client = host.GetTestClient();

        using var response = await client.GetAsync("/admin/Messages/Inbox");

        Assert.True(
            response.StatusCode is HttpStatusCode.Redirect
                or HttpStatusCode.Found
                or HttpStatusCode.TemporaryRedirect,
            $"Expected a 302-family redirect but got {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task InboxHttp_EditorOnlyRequest_Returns404()
    {
        using var host = await BuildHostAsync(TestPrincipal.EditorOnly);
        var client = host.GetTestClient();

        using var response = await client.GetAsync("/admin/Messages/Inbox");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Sort/filter/paginate + view model ---

    [Fact]
    public async Task Inbox_DefaultInvocation_ReturnsPagedListSortedNewestFirst()
    {
        await TruncateMessagesAsync();
        await using var context = _fixture.CreateContext();
        var messages = new DbSearchMessageService(context);
        for (var i = 0; i < 22; i++)
        {
            await messages.CreateAsync($"s-{i}", $"looking for widget number {i}", null, null, CancellationToken.None);
            await Task.Delay(1);
        }

        var controller = BuildController(messages, pageSize: 10);

        var result = await controller.Inbox(sort: null, dir: null, filter: null, page: 1, ct: CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MessagesInboxViewModel>(view.Model);
        Assert.Equal(22, model.TotalCount);
        Assert.Equal(10, model.Rows.Count);
        Assert.Equal("submittedAt", model.Sort);
        Assert.Equal("desc", model.Dir);
        Assert.Equal(3, model.TotalPages);
    }

    [Fact]
    public async Task Inbox_SortByIsReadAsc_ReordersUnreadFirst()
    {
        await TruncateMessagesAsync();
        await using var context = _fixture.CreateContext();
        var messages = new DbSearchMessageService(context);
        var read1 = await messages.CreateAsync("s-r1", "read msg 1", null, null, CancellationToken.None);
        await messages.CreateAsync("s-u1", "unread msg 1", null, null, CancellationToken.None);
        var read2 = await messages.CreateAsync("s-r2", "read msg 2", null, null, CancellationToken.None);
        await messages.CreateAsync("s-u2", "unread msg 2", null, null, CancellationToken.None);
        await messages.MarkReadAsync(read1, "admin", DateTime.UtcNow, CancellationToken.None);
        await messages.MarkReadAsync(read2, "admin", DateTime.UtcNow, CancellationToken.None);

        var controller = BuildController(messages, pageSize: 20);

        var result = await controller.Inbox(sort: "isRead", dir: "asc", filter: null, page: 1, ct: CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MessagesInboxViewModel>(view.Model);
        Assert.Equal(4, model.TotalCount);
        Assert.False(model.Rows[0].IsRead);
        Assert.False(model.Rows[1].IsRead);
        Assert.True(model.Rows[2].IsRead);
        Assert.True(model.Rows[3].IsRead);
        Assert.Equal("isRead", model.Sort);
        Assert.Equal("asc", model.Dir);
    }

    [Fact]
    public async Task Inbox_Filter_NarrowsResultsToMatchingRows()
    {
        await TruncateMessagesAsync();
        await using var context = _fixture.CreateContext();
        var messages = new DbSearchMessageService(context);
        await messages.CreateAsync("s-a", "widget report", null, null, CancellationToken.None);
        await messages.CreateAsync("s-b", "cannot find anything", null, null, CancellationToken.None);
        await messages.CreateAsync("s-c", "another widget question", null, null, CancellationToken.None);

        var controller = BuildController(messages, pageSize: 20);

        var result = await controller.Inbox(sort: null, dir: null, filter: "widget", page: 1, ct: CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MessagesInboxViewModel>(view.Model);
        Assert.Equal(2, model.TotalCount);
        Assert.Equal("widget", model.Filter);
    }

    // --- View-render assertions on Views/Admin/Messages/Inbox.cshtml ---

    [Fact]
    public async Task InboxView_RendersTableWithColumnHeadersAndSortLinks()
    {
        var model = new MessagesInboxViewModel
        {
            Rows = new[]
            {
                new SearchMessageSummary(1, DateTime.UtcNow, "session-abcdef1234567890", true, false, "widget preview"),
            },
            TotalCount = 1,
            Page = 1,
            PageSize = 20,
            Sort = "submittedAt",
            Dir = "desc",
            Filter = null,
        };

        var html = await RenderViewAsync("/Views/Admin/Messages/Inbox.cshtml", model);

        Assert.Contains("Messages inbox", html);
        Assert.Contains("govuk-table", html);
        Assert.Contains("Submitted", html);
        Assert.Contains("Session", html);
        Assert.Contains("Read", html);
        // Session id displayed as first-8-chars… with the full value in the abbr title.
        Assert.Contains("session-", html);
        Assert.Contains("<abbr", html);
        Assert.Contains("session-abcdef1234567890", html);
        // Has-email badge renders when HasEmail is true.
        Assert.Contains(">Email<", html);
        // New badge renders when unread.
        Assert.Contains(">New<", html);
        // First-line preview renders inside the link to the detail page.
        Assert.Contains("/admin/Messages/Inbox/1", html);
        Assert.Contains("widget preview", html);
        // Sort links exist for the sortable columns.
        Assert.Contains("?sort=submittedAt", html);
        Assert.Contains("?sort=isRead", html);
    }

    [Fact]
    public async Task InboxView_EmptyState_RendersNoMessagesMessage()
    {
        var model = new MessagesInboxViewModel
        {
            Rows = Array.Empty<SearchMessageSummary>(),
            TotalCount = 0,
            Page = 1,
            PageSize = 20,
            Sort = "submittedAt",
            Dir = "desc",
            Filter = null,
        };

        var html = await RenderViewAsync("/Views/Admin/Messages/Inbox.cshtml", model);

        Assert.Contains("No messages", html);
    }

    // -----------------------------------------------------------------
    // Helper plumbing
    // -----------------------------------------------------------------

    private static MessagesController BuildController(ISearchMessageService messages, int pageSize)
    {
        var settings = Substitute.For<ISettingService>();
        settings.GetIntAsync(SettingKeys.CmsPageLength).Returns(pageSize);

        var controller = new MessagesController(messages, settings, currentUserService: null);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
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

    internal static async Task<string> RenderViewAsync<TModel>(string viewPath, TModel model)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddControllersWithViews()
                        .AddApplicationPart(typeof(MessagesController).Assembly);
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
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        var view = viewEngine.GetView(executingFilePath: null, viewPath: viewPath, isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate {viewPath}. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var viewData = new ViewDataDictionary<TModel>(
            new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);

        await using var writer = new StringWriter();
        var viewContext = new ViewContext(
            actionContext, view.View, viewData, tempData, writer, new HtmlHelperOptions());
        await view.View.RenderAsync(viewContext);
        return writer.ToString();
    }

    // ---- HTTP host + auth stub (mirrors SearchAnalyticsAuthTests) ----

    private enum TestPrincipal { Anonymous, EditorOnly }

    private async Task<IHost> BuildHostAsync(TestPrincipal principal)
    {
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddDbContext<PortalDbContext>(o => o.UseNpgsql(_fixture.ConnectionString));
                services.AddScoped<IPortalDbContext>(sp => sp.GetRequiredService<PortalDbContext>());
                services.AddScoped<ISearchMessageService, DbSearchMessageService>();
                services.AddScoped<ISettingService>(_ =>
                {
                    var s = Substitute.For<ISettingService>();
                    s.GetIntAsync(Arg.Any<string>()).Returns(10);
                    return s;
                });

                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<TestAuthOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                services.Configure<TestAuthOptions>(TestAuthHandler.SchemeName, o => o.Principal = principal);
                services.AddAuthentication().AddCookie(
                    Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme,
                    o => o.LoginPath = "/sign-in");
                services.AddAuthorization();

                services.AddScoped<IAdminAccessPolicy>(_ =>
                {
                    var policy = Substitute.For<IAdminAccessPolicy>();
                    policy.CanAccessAsync(
                            Arg.Is<ClaimsPrincipal>(p => p.HasClaim(ClaimTypes.Role, "cypmd_editor")
                                                        && !p.HasClaim(ClaimTypes.Role, "cypmd_admin")),
                            Arg.Any<string>())
                        .Returns(false);
                    policy.CanAccessAsync(
                            Arg.Is<ClaimsPrincipal>(p => p.HasClaim(ClaimTypes.Role, "cypmd_admin")),
                            Arg.Any<string>())
                        .Returns(true);
                    return policy;
                });

                services.AddControllers()
                    .AddApplicationPart(typeof(MessagesController).Assembly);
            });
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            });
        });

        return await builder.StartAsync();
    }

    private sealed class TestAuthOptions : AuthenticationSchemeOptions
    {
        public TestPrincipal Principal { get; set; }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<TestAuthOptions>
    {
        public const string SchemeName = "TestScheme";

        public TestAuthHandler(
            IOptionsMonitor<TestAuthOptions> options,
            Microsoft.Extensions.Logging.ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Options.Principal == TestPrincipal.Anonymous)
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = Options.Principal switch
            {
                TestPrincipal.EditorOnly => new[]
                {
                    new Claim(ClaimTypes.Name, "editor"),
                    new Claim(ClaimTypes.Role, "cypmd_editor"),
                },
                _ => Array.Empty<Claim>(),
            };

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.Headers.Location = "/sign-in";
            Response.StatusCode = (int)HttpStatusCode.Redirect;
            return Task.CompletedTask;
        }
    }
}
