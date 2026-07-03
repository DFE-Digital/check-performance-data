using AngleSharp;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Infrastructure;
using DfE.CheckPerformanceData.Web.Authentication;
using DfE.CheckPerformanceData.Web.Diagnostics;
using DfE.CheckPerformanceData.Web.Services;
using DfE.CheckPerformanceData.Persistence;
using DfE.CheckPerformanceData.Persistence.Seeding;
using DfE.CheckPerformanceData.Web.Extensions;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.FileStorage;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Infrastructure.Queue;
using DfE.CheckPerformanceData.Web.Seeding;
using DfE.CheckPerformanceData.Web.Controllers.Journey;
using DfE.CheckPerformanceData.Web.QuestionFlow;
using DfE.CheckPerformanceData.Web.Settings;
using DfE.CheckPerformanceData.Web.PageTree;
using GovUk.Frontend.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Templates;
using Serilog.Templates.Themes;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting application");

    var builder = WebApplication.CreateBuilder(args);

    // WebApplication.CreateBuilder already configures the configuration sources in
    // the correct precedence (last wins): appsettings.json, appsettings.{Environment}.json,
    // user secrets (Development), environment variables, command line. Re-adding
    // appsettings.json here previously landed it after appsettings.{Environment}.json
    // and silently clobbered environment-specific overrides.
    var configuration = builder.Configuration;

    builder.Host.UseSerilog((context, services, config) =>
    {
        var isDevelopment = context.HostingEnvironment.IsDevelopment();

        config
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console(isDevelopment
                ? new ExpressionTemplate(
                    "[{@t:HH:mm:ss} {@l:u3}] {SourceContext}\n  {@m}\n{@x}",
                    theme: TemplateTheme.Code)
                : new CompactJsonFormatter());
    });
    
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        options.KnownProxies.Clear();
    });

    builder.Services.AddHttpContextAccessor();
   
    builder.Services.Configure<GtmSettings>(builder.Configuration.GetSection("GoogleTagManager"));
    builder.Services.Configure<ClaritySettings>(builder.Configuration.GetSection("Clarity"));
    var seedData = builder.Environment.IsDevelopment() || configuration["SeedDevelopmentData"] == "true";
    
    builder.Services
        .AddDfeApiClient(builder.Configuration)
        .AddDfeSignInAuthentication(builder.Configuration)
        .AddGovUkFrontend()
        .AddPersistenceDependencies(configuration, seedData)
        .AddApplicationDependencies()
        .AddNotifyService(builder.Configuration)
        .AddAdminNavEntries(includeDangerZone: !builder.Environment.IsProduction());

    // Orchestrates the full dev-data seeding sequence, shared by startup seeding (below) and
    // the admin Danger zone "Reset seed data" action.
    builder.Services.AddScoped<IDevDataSeedingOrchestrator, DevDataSeedingOrchestrator>();

    builder.Services.AddScoped<IEmailLinkGenerator, DfE.CheckPerformanceData.Web.Notify.EmailLinkGenerator>();

    // Dev-only impersonation: a second auth scheme + a policy scheme that picks between
    // it and the real DfE cookie scheme based on which cookie is present. Registered
    // ONLY where the dev tooling surface is enabled (Dev:ToolsEnabled) — i.e. local dev
    // and ephemeral PR/review apps — AND never in Production. This matches the gate used
    // by the sibling /dev/* controllers (DevQueueSeed, DevPipeline, DevUat), so deployed
    // DEV/QA/Preproduction (which never set Dev:ToolsEnabled) cannot serve these routes or
    // carry the marker cookie. The IsProduction() guard is belt-and-braces on top of the
    // flag. Don't move this block outside either condition.
    if (!builder.Environment.IsProduction()
        && configuration.GetValue<bool>(SettingKeys.DevToolsEnabled))
    {
        const string DevAwareScheme = "DevAware";

        builder.Services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, DevImpersonationAuthHandler>(
                DevImpersonationConstants.Scheme,
                _ => { })
            .AddPolicyScheme(DevAwareScheme, DevAwareScheme, options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    // Prefer the real DfE Sign-In auth cookie if present so an
                    // already-signed-in manual tester keeps their real claims
                    // (organisationid, name, etc.) when they flip the impersonation
                    // cookie. The transformer then overlays the editor role on top.
                    // Only fall back to the synthetic DevImpersonation scheme when
                    // there's no real session — the E2E case.
                    if (context.Request.Cookies.ContainsKey(".AspNetCore.Cookies"))
                        return CookieAuthenticationDefaults.AuthenticationScheme;
                    if (context.Request.Cookies.ContainsKey(DevImpersonationConstants.CookieName))
                        return DevImpersonationConstants.Scheme;
                    return CookieAuthenticationDefaults.AuthenticationScheme;
                };
            });

        // Override only DefaultAuthenticateScheme so [Authorize] checks pass through
        // the policy scheme. DefaultChallengeScheme stays as OpenIdConnect so DfE
        // Sign-In still triggers for unauthenticated users; DefaultSignInScheme stays
        // as Cookies so the OIDC callback still writes the real auth cookie.
        builder.Services.Configure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = DevAwareScheme;
        });

        builder.Services.AddScoped<IClaimsTransformation, DevImpersonationClaimsTransformer>();
    }

    builder.Services.AddScoped<IRequestNotificationService, DfE.CheckPerformanceData.Infrastructure.Notify.RequestNotificationService>();
    builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
    builder.Services.AddScoped<IFileStorageService, EvidenceBlobStorageService>();
    builder.Services.AddScoped<JourneyViewModelBuilder>();

    builder.Services.Configure<QueueOptions>(builder.Configuration.GetSection("QueueOptions"));
    builder.Services.AddScoped<IQueueService, PostgresQueueService>();
    builder.Services.AddScoped<IQueueAdminService, QueueAdminService>();
    builder.Services.AddScoped<DfE.CheckPerformanceData.Application.Observability.SubmittedMetricRecorder>();
    builder.Services.AddScoped<DfE.CheckPerformanceData.Web.Controllers.DevPipelineRunner>();
    builder.Services.AddScoped<DfE.CheckPerformanceData.Web.Services.GuidanceContentCopyService>();
    builder.Services.AddSingleton<PayloadRedactor>();

    builder.Services.AddSingleton<IReservedRouteProvider, EndpointReservedRouteProvider>();
    builder.Services.AddScoped<PageNodePathValidator>();

    builder.Services.AddSingleton(_ =>
        new BlobServiceClient(builder.Configuration.GetConnectionString("AzureStorage")));

    builder.Services.AddSingleton<IReadOnlyDictionary<string, BlobServiceClient>>(_ =>
    {
        var clients = new Dictionary<string, BlobServiceClient>();
        var appConn = builder.Configuration.GetConnectionString("AzureStorage");
        if (!string.IsNullOrEmpty(appConn))
            clients["app"] = new BlobServiceClient(appConn);
        var ingressConn = builder.Configuration.GetConnectionString("IngressStorage");
        if (!string.IsNullOrEmpty(ingressConn))
            clients["ingress"] = new BlobServiceClient(ingressConn);
        return clients;
    });
    builder.Services.Configure<DfE.CheckPerformanceData.Infrastructure.RulesEngine.BlobRulesProviderOptions>(
        builder.Configuration.GetSection(
            DfE.CheckPerformanceData.Infrastructure.RulesEngine.BlobRulesProviderOptions.SectionName));
    builder.Services.TryAddSingleton(TimeProvider.System);
    builder.Services.AddScoped<
        DfE.CheckPerformanceData.Application.RulesConfig.IRulesConfigStore,
        DfE.CheckPerformanceData.Infrastructure.RulesEngine.BlobRulesConfigStore>();
    // Lets the dev-data seeding orchestrator self-seed the rules-config blobs in dev/E2E. In
    // deployed environments the rules-engine worker seeds them; that worker isn't part of the
    // local web stack, so without this the admin rules editor 404s on a fresh environment.
    builder.Services.AddScoped<DfE.CheckPerformanceData.Infrastructure.RulesEngine.RulesConfigSeeder>();
    // TODO: revert to QuestionFlowBlobClient once storage permissions are configured for deployed environments
    //if (builder.Environment.IsDevelopment())
        builder.Services.AddSingleton<IQuestionFlowBlobClient, QuestionFlowBlobClient>();
    // else
    //     builder.Services.AddSingleton<IQuestionFlowBlobClient>(_ =>
    //         new FileSystemQuestionFlowClient(builder.Environment.ContentRootPath));
    builder.Services.AddScoped<IRequestBlobClient, RequestBlobClient>();
    builder.Services.AddScoped<IRequestStateBlobClient, RequestStateBlobClient>();
    builder.Services.AddScoped<IPupilDataBlobClient, PupilDataBlobClient>();

    builder.Services.AddAntiforgery(options =>
    {
        options.HeaderName = "X-XSRF-TOKEN";
    });

    builder.Services.AddControllersWithViews();

    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    });

    builder.Services.AddMemoryCache();
    builder.Services.AddDistributedMemoryCache();
    builder.Services.AddSession(options =>
    {
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    });

    builder.Services.AddHealthChecks();

    var app = builder.Build();

    await app.MigrateDatabaseAsync();

    using (var scope = app.Services.CreateScope())
    {
        await scope.ServiceProvider.GetRequiredService<DefaultPageNodeSeeder>().SeedAsync();
        await scope.ServiceProvider
            .GetRequiredService<DfE.CheckPerformanceData.Application.Admin.DefaultAdminAccessSeeder>()
            .SeedIfEmptyAsync();
    }

    app.UseForwardedHeaders();

    app.UseSerilogRequestLogging(options =>
    {
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestPath", httpContext.Request.Path);
            diagnosticContext.Set("StatusCode", httpContext.Response.StatusCode);
            diagnosticContext.Set("RequestMethod", httpContext.Request.Method);
            diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
        };
    });

    app.UseGovUkFrontend();

    app.MapHealthChecks("/healthcheck").AllowAnonymous();

// Configure the HTTP request pipeline.
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }

    if (seedData)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDevDataSeedingOrchestrator>().RunAsync();
    }

    app.UseHttpsRedirection();

    app.Use(async (context, next) =>
    {
        context.Response.Headers.Append("Content-Security-Policy",
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://*.googletagmanager.com https://*.google-analytics.com https://*.analytics.google.com https://*.clarity.ms; " +
            "style-src 'self' 'unsafe-inline' https://*.googletagmanager.com https://fonts.googleapis.com; " +
            "img-src 'self' data: blob: https://*.googletagmanager.com https://*.google-analytics.com https://*.analytics.google.com https://*.clarity.ms https://fonts.gstatic.com; " +
            "font-src 'self' data: https://fonts.gstatic.com; " +
            "connect-src 'self' https://*.googletagmanager.com https://*.google-analytics.com https://*.analytics.google.com https://*.clarity.ms; " +
            "frame-src 'self' https://*.googletagmanager.com; " +
            "object-src 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'");
        await next();
    });

    app.UseSession();

    app.UseRouting();

    app.UseAuthentication();
    app.UseAuthorization();

    // Sits after auth so the diagnostic comment sees the final principal claims;
    // before controllers so it can wrap their response body. The middleware itself
    // is a no-op when env.IsProduction() or when Diagnostics:ShowSessionFooter
    // is false / unset.
    app.UseMiddleware<DiagnosticFooterMiddleware>();

    app.MapStaticAssets().AllowAnonymous();

    // Wiki is retired: /help and its descendants now resolve via PageController's
    // catch-all against the PageNode tree (e.g. help/not-found is the default 404 page).
    // Wiki management endpoints on HelpController (help/create, help/search, help/deleted,
    // help/versions/{id}, etc.) still work — they use explicit HttpGet/HttpPost attribute
    // routes, so removing the catch-all map here doesn't affect them.

    app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}")
        .WithStaticAssets();


    app.Run();
}
catch (Exception e)
{
    Log.Fatal(e, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();   
}