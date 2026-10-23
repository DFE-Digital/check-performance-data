using System.Net;
using GovUk.Frontend.AspNetCore;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using System.Net.Http.Json;
using System.Security.Claims;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Impersonation;
using DfE.CheckPerformanceData.Infrastructure.Authentication;
using DfE.CheckPerformanceData.Infrastructure.Impersonation;
using DfE.CheckPerformanceData.Web.Impersonation;
using DfE.CheckPerformanceData.Web.Middleware;
using DfE.CheckPerformanceData.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.Impersonation;

public sealed class ImpersonationSessionTests
{
    [Fact]
    public async Task Expired_session_data_cannot_restore_selection_or_authorise_an_old_form()
    {
        await using var app = await BuildAsync();
        var browser = new Browser(app.GetTestClient());
        await browser.SendAsync(HttpMethod.Get, "/login");
        await browser.PostAsync("/start", await browser.ReadAsync());
        var viewing = await browser.ReadAsync();
        Assert.True(viewing.Active);
        await browser.SendAsync(HttpMethod.Get, "/expire-session");
        var after = await browser.ReadAsync();
        Assert.False(after.Active);
        Assert.Equal("100001", after.Urn);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PostAsync("/write", viewing)).StatusCode);
    }
    [Fact]
    public async Task Starting_impersonation_waits_for_an_authorised_original_establishment_write()
    {
        await using var app = await BuildAsync();
        var browser = new Browser(app.GetTestClient());
        await browser.SendAsync(HttpMethod.Get, "/login");
        var before = await browser.ReadAsync();
        var probe = app.Services.GetRequiredService<WriteProbe>();
        var write = browser.PostAsync("/write?hold=true", before);
        await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var transition = new Browser(app.GetTestClient(), browser.Cookies).PostAsync("/start", before);
        await app.Services.GetRequiredService<SerialTestGate>().Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(transition.IsCompleted);
        probe.Release.TrySetResult();
        var response = await write;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("100001", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await transition).StatusCode);
        Assert.True((await browser.ReadAsync()).Active);
    }
    [Fact]
    public async Task Real_banner_encodes_manual_identifiers_and_hides_admin_and_content_editing()
    {
        await using var app = await BuildAsync();
        var browser = new Browser(app.GetTestClient());
        await browser.SendAsync(HttpMethod.Get, "/login");
        var before = await browser.ReadAsync();
        await browser.PostAsync("/start?target=" + Uri.EscapeDataString("<script>alert(1)</script>"), before);
        var response = await browser.PostAsync("/write", await browser.ReadAsync());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("impersonation-banner", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("Exit impersonation", html);
        Assert.Contains("URN", html);
        Assert.DoesNotContain("href=\"/admin\"", html);
        Assert.DoesNotContain("content-block__edit-link", html);
        if (Environment.GetEnvironmentVariable("CPD_IMPERSONATION_PREVIEW") is { } preview)
            await File.WriteAllTextAsync(preview, html);
        await app.Services.GetRequiredService<IContentBlockService>().DidNotReceiveWithAnyArgs()
            .EnsureAsync(default!, default!, default!, default!);
    }
    [Fact]
    public async Task Real_cookie_sessions_share_across_tabs_block_writes_and_reject_stale_forms_after_exit()
    {
        await using var app = await BuildAsync();
        var browser = new Browser(app.GetTestClient());
        Assert.Equal(HttpStatusCode.OK, (await browser.SendAsync(HttpMethod.Get, "/login")).StatusCode);
        var before = await browser.ReadAsync();
        var validWrite = await browser.PostAsync("/write", before);
        Assert.Equal(HttpStatusCode.OK, validWrite.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.PostAsync("/start", before)).StatusCode);
        var viewing = await browser.ReadAsync();
        Assert.True(viewing.Active);
        Assert.Equal("7654321", viewing.Laestab);
        Assert.Equal("100002", viewing.Urn);
        Assert.Equal("100001", viewing.OriginalUrn);

        var secondTab = new Browser(app.GetTestClient(), browser.Cookies);
        Assert.True((await secondTab.ReadAsync()).Active);
        Assert.Equal(HttpStatusCode.Forbidden, (await secondTab.PostAsync("/write", viewing)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await secondTab.SendAsync(HttpMethod.Get, "/mutating-get")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await secondTab.SendAsync(HttpMethod.Get, "/admin-page")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await secondTab.SendAsync(HttpMethod.Get, "/css/test.css")).StatusCode);

        var separate = new Browser(app.GetTestClient());
        await separate.SendAsync(HttpMethod.Get, "/login");
        Assert.False((await separate.ReadAsync()).Active);
        Assert.Equal(HttpStatusCode.Conflict, (await separate.PostAsync("/write", viewing)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await browser.PostAsync("/exit", viewing)).StatusCode);
        var after = await secondTab.ReadAsync();
        Assert.False(after.Active);
        Assert.Equal("100001", after.Urn);
        Assert.Equal(HttpStatusCode.Conflict, (await secondTab.PostAsync("/write", viewing)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await secondTab.PostAsync("/write", before)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await secondTab.PostAsync("/write", after)).StatusCode);
    }

    [Fact]
    public async Task Sign_out_and_new_login_cannot_inherit_selection_and_cross_site_start_is_rejected()
    {
        await using var app = await BuildAsync();
        var browser = new Browser(app.GetTestClient());
        await browser.SendAsync(HttpMethod.Get, "/login");
        var before = await browser.ReadAsync();
        var forged = before with { AntiForgery = "invalid-antiforgery" };
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/start", forged)).StatusCode);
        Assert.False((await browser.ReadAsync()).Active);
        await browser.PostAsync("/start", before);
        Assert.True((await browser.ReadAsync()).Active);
        await browser.SendAsync(HttpMethod.Get, "/signout");
        await browser.SendAsync(HttpMethod.Get, "/login");
        Assert.False((await browser.ReadAsync()).Active);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("impersonation")]
    [InlineData("neither")]
    public async Task Either_role_alone_cannot_start(string roles)
    {
        await using var app = await BuildAsync();
        var browser = new Browser(app.GetTestClient());
        await browser.SendAsync(HttpMethod.Get, "/login?roles=" + roles);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsync("/start", await browser.ReadAsync())).StatusCode);
    }

    private static async Task<WebApplication> BuildAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddDataProtection();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSession();
        builder.Services.AddAntiforgery();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<DistributedCacheTicketStore>();
        builder.Services.AddAuthentication("Cookies").AddCookie("Cookies", options => { });
        builder.Services.AddOptions<CookieAuthenticationOptions>("Cookies").Configure<DistributedCacheTicketStore>((options, tickets) => options.SessionStore = tickets);
        builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
        builder.Services.AddScoped<ImpersonationSessionService>();
        builder.Services.AddScoped<IImpersonationSessionStore, DistributedImpersonationSessionStore>();
        builder.Services.AddSingleton<IImpersonationSessionLock, SerialTestGate>();
        builder.Services.AddSingleton(sp => (SerialTestGate)sp.GetRequiredService<IImpersonationSessionLock>());
        builder.Services.AddSingleton<WriteProbe>();
        builder.Services.AddSingleton(Substitute.For<IImpersonationAuditWriter>());
        builder.Services.AddSingleton(Substitute.For<ICheckYourPupilDataRepository>());
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(DfE.CheckPerformanceData.Web.Controllers.ImpersonationController).Assembly)
            .AddApplicationPart(typeof(GovUkFrontendExtensions).Assembly);
        builder.Services.AddGovUkFrontend();
        builder.Services.AddSingleton(Substitute.For<IContentBlockService>());
        builder.Services.AddScoped<IEstablishmentViewContext>(sp => sp.GetRequiredService<ImpersonationSessionService>());
        var assets = Substitute.For<DfE.CheckPerformanceData.Application.SiteAssets.ISiteAssetService>();
        assets.GetAsync().Returns(new DfE.CheckPerformanceData.Application.SiteAssets.SiteAssetContent("", "", false, false));
        builder.Services.AddSingleton(assets);
        var app = builder.Build();
        app.UseSession();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<ImpersonationContextMiddleware>();
        app.MapGet("/login", async (HttpContext context) =>
        {
            var roleSet = context.Request.Query["roles"].ToString();
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "original-admin"), new("organisation_urn", "100001"), new("organisation_laestab", "1234567") };
            if (roleSet is "" or "admin") claims.Add(new(ClaimTypes.Role, "cypmd_admin"));
            if (roleSet is "" or "impersonation") claims.Add(new(ClaimTypes.Role, "cypmd_impersonation"));
            if (roleSet is "") claims.Add(new(ClaimTypes.Role, "cypmd_editor"));
            await context.SignInAsync("Cookies", new(new ClaimsIdentity(claims, "Cookies")));
            return Results.Ok();
        });
        app.MapGet("/read", (HttpContext context, ImpersonationSessionService session, ICurrentUserService original, IAntiforgery antiforgery) =>
            Results.Json(new ReadState(session.IsImpersonating, session.OrganisationLaestab, session.OrganisationUrn, original.OrganisationUrn,
                session.CreateStamp(), antiforgery.GetAndStoreTokens(context).RequestToken)))
            .WithMetadata(Action("CheckYourPupilData", "Index"));
        app.MapPost("/start", async (HttpContext context, ImpersonationSessionService session, IAntiforgery antiforgery) =>
        {
            if (!ImpersonationAccessPolicy.CanAccess(context.User)) return Results.StatusCode(403);
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            var target = context.Request.Query["target"].ToString();
            await session.SetSelectionAsync(new(target.Length == 0 ? "7654321" : target, "100002", 11, 18), default);
            return Results.Ok();
        }).WithMetadata(Action("Impersonation", "Start"));
        app.MapPost("/exit", async (HttpContext context, ImpersonationSessionService session, IAntiforgery antiforgery) =>
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            await session.SetSelectionAsync(null, default);
            return Results.Ok();
        }).WithMetadata(Action("Impersonation", "Exit"));
        app.MapPost("/write", async (HttpContext context, ImpersonationSessionService session, ICurrentUserService original, WriteProbe probe) =>
        {
            await session.EnsureCanWriteAsync(Guid.NewGuid());
            if (context.Request.Query["hold"] == "true")
            {
                probe.Entered.TrySetResult();
                await probe.Release.Task.WaitAsync(context.RequestAborted);
            }
            return Results.Text(original.OrganisationUrn);
        }).WithMetadata(Action("ConfirmCorrect", "Submit"));
        app.MapGet("/mutating-get", () => Results.Ok()).WithMetadata(Action("Journey", "StartAddingPupil"));
        app.MapGet("/admin-page", () => Results.Ok()).WithMetadata(Action("Admin", "Index"));
        // Simulate the ordinary session record disappearing at its existing expiry.
        app.MapGet("/expire-session", (HttpContext context) => { context.Session.Clear(); return Results.Ok(); })
            .WithMetadata(Action("Home", "Index"));
        app.MapGet("/css/test.css", () => Results.Text("body {}"));
        app.MapGet("/signout", async (HttpContext context) => { await context.SignOutAsync("Cookies"); return Results.Ok(); })
            .WithMetadata(Action("DfeSignOut", "Index"));
        await app.StartAsync();
        return app;
    }

    private static ControllerActionDescriptor Action(string controller, string action) => new() { ControllerName = controller, ActionName = action };
    private sealed record ReadState(bool Active, string Laestab, string Urn, string OriginalUrn, string? Stamp, string? AntiForgery);

    private sealed class Browser(HttpClient client, Dictionary<string, string>? shared = null)
    {
        public Dictionary<string, string> Cookies { get; } = shared ?? new();
        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content = null)
        {
            var request = new HttpRequestMessage(method, url) { Content = content };
            request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", Cookies.Select(p => p.Key + "=" + p.Value)));
            var response = await client.SendAsync(request);
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                foreach (var cookie in cookies)
                {
                    var pair = cookie.Split(';')[0].Split('=', 2);
                    Cookies[pair[0]] = pair[1];
                }
            return response;
        }
        public async Task<ReadState> ReadAsync() => (await (await SendAsync(HttpMethod.Get, "/read")).Content.ReadFromJsonAsync<ReadState>())!;
        public Task<HttpResponseMessage> PostAsync(string url, ReadState state) => SendAsync(HttpMethod.Post, url,
            new FormUrlEncodedContent(new Dictionary<string, string> { ["EstablishmentContext"] = state.Stamp ?? "", ["__RequestVerificationToken"] = state.AntiForgery ?? "" }));
    }
    private sealed class SerialTestGate : IImpersonationSessionLock
    {
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _gates = new();
        public async Task<IAsyncDisposable> AcquireAsync(string binding, CancellationToken ct)
        {
            var semaphore = _gates.GetOrAdd(binding, _ => new(1));
            if (semaphore.CurrentCount == 0) Waiting.TrySetResult();
            await semaphore.WaitAsync(ct);
            return new Lease(semaphore);
        }
        private sealed class Lease(SemaphoreSlim semaphore) : IAsyncDisposable
        {
            public ValueTask DisposeAsync() { semaphore.Release(); return ValueTask.CompletedTask; }
        }
    }
    private sealed class WriteProbe
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
