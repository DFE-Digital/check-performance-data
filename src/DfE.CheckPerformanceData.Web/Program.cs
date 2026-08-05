using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Infrastructure;
using DfE.CheckPerformanceData.Web.Diagnostics;
using DfE.CheckPerformanceData.Web.Middleware;
using DfE.CheckPerformanceData.Web.Services;
using DfE.CheckPerformanceData.Persistence;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Seeding;
using DfE.CheckPerformanceData.Web.Extensions;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.FileStorage;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Infrastructure.Queue;
using DfE.CheckPerformanceData.Web.Seeding;
using DfE.CheckPerformanceData.Web.Controllers.Journey;
using DfE.CheckPerformanceData.Web.PageTree;
using DfE.CheckPerformanceData.Web.Analytics;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Infrastructure.Analytics;
using Dfe.Analytics;
using Dfe.Analytics.AspNetCore;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using GovUk.Frontend.AspNetCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using Serilog.Formatting.Compact;
using DfE.CheckPerformanceData.Web.Startup;

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

    builder.UseCpdSerilog();

    builder.AddCpdCoreWeb();

    var seedData = builder.Environment.IsDevelopment() || configuration["SeedDevelopmentData"] == "true";

    builder.Services
        .AddCpdSettings(configuration)
        .AddDfeApiClient(builder.Configuration)
        .AddDfeSignInAuthentication(builder.Configuration)
        .AddGovUkFrontend()
        .AddPersistenceDependencies(configuration)
        .AddApplicationDependencies()
        .AddNotifyService(builder.Configuration)
        .AddAdminNavEntries(includeDangerZone: !builder.Environment.IsProduction())
        .AddCpdSearchTelemetry()
        .AddCpdNotifications()
        .AddCpdAppLogSink(configuration);

    // Orchestrates the full dev-data seeding sequence, shared by startup seeding (below) and
    // the admin Danger zone "Reset seed data" action.
    builder.Services.AddScoped<IDevDataSeedingOrchestrator, DevDataSeedingOrchestrator>();

    builder.AddCpdDevImpersonation();

    builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
    builder.Services.AddScoped<IFileStorageService, EvidenceBlobStorageService>();
    builder.Services.AddScoped<IJourneyViewModelBuilder, JourneyViewModelBuilder>();

    builder.Services.Configure<QueueOptions>(builder.Configuration.GetSection("QueueOptions"));
    builder.Services.AddScoped<IQueueService, PostgresQueueService>();
    builder.Services.AddScoped<IQueueAdminService, QueueAdminService>();
    builder.Services.AddScoped<DfE.CheckPerformanceData.Application.Observability.SubmittedMetricRecorder>();
    builder.Services.AddScoped<DfE.CheckPerformanceData.Web.Controllers.DevPipelineRunner>();
    builder.Services.AddScoped<DfE.CheckPerformanceData.Web.Services.GuidanceContentCopyService>();
    builder.Services.AddSingleton<PayloadRedactor>();

    builder.Services.AddSingleton<IReservedRouteProvider, EndpointReservedRouteProvider>();
    builder.Services.AddScoped<PageNodePathValidator>();

    builder.AddCpdDataProtection();

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
    builder.Services.AddScoped<ICsvSchemaFileProcessor, CsvSchemaFileProcessor>();

    builder.AddCpdSessionStore();

    // DfE Analytics: stream a web_request event to BigQuery per request when configured.
    // Deployed envs wire DfeAnalytics:* via Terraform; guarded on DatasetId so dev,
    // review and local boot without GCP. The matching middleware is added below under
    // the same flag. The RequestFilter (see AnalyticsRequestFilter) keeps health probes,
    // static assets and scanner/bot noise out of the dataset.
    var analyticsEnabled = !string.IsNullOrEmpty(builder.Configuration["DfeAnalytics:DatasetId"]);
    if (analyticsEnabled)
    {
        builder.Services
            .AddDfeAnalytics()
            .AddAspNetCoreIntegration(options =>
                options.RequestFilter = AnalyticsRequestFilter.ShouldTrack);

        builder.Services.AddSingleton<IWebRequestEventEnricher, OrganisationEventEnricher>();
        // Custom events go through the same IEventSender (AspNetCoreEventSender), so each
        // is sent as its own row, auto-enriched with request + organisation context.
        builder.Services.AddTransient<IAnalyticsService, DfeAnalyticsService>();
    }
    else
    {
        // No-op so controllers can always inject IAnalyticsService; dev/review/local
        // boot without GCP.
        builder.Services.AddSingleton<IAnalyticsService, NullAnalyticsService>();
    }

    var app = builder.Build();

    await app.MigrateDatabaseAsync();

    using (var scope = app.Services.CreateScope())
    {
        // Countries back the country autocomplete and must exist in every environment,
        // including Production. Seeded idempotently and content-aware: a no-op when the table
        // already matches the embedded seed data, a full reseed when the CSV/entries change.
        // Safe to run unconditionally on every startup, unlike the dev-only data seeding below.
        await SeedCountries.ExecuteSeed(scope.ServiceProvider.GetRequiredService<IPortalDbContext>());

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

    // Sits immediately after UseSession() so it can call Session.LoadAsync(); its
    // SetString on first access is what commits the session cookie (framework
    // lazy-writes on first store mutation). Downstream consumers therefore see a
    // stable Session.Id across requests. Also enforces the server-side absolute
    // lifetime cap that Cookie.MaxAge (a browser-side hint only) cannot.
    app.UseMiddleware<SessionAbsoluteLifetimeMiddleware>();

    app.UseRouting();

    // Re-executes unmapped-route 404 responses through the MVC pipeline so users see a
    // text/html page (with the shared layout, and therefore the injected session
    // comment) instead of the framework default text/plain "Status Code: 404" body.
    // Sits after UseRouting per the framework's placement contract for status-code
    // page middleware.
    app.UseStatusCodePagesWithReExecute("/Home/NotFound");

    app.UseAuthentication();
    app.UseAuthorization();

    // After auth so the event captures the signed-in user's id + organisation claims;
    // the RequestFilter configured above excludes health probes from the dataset.
    if (analyticsEnabled)
        app.UseDfeAnalytics();

    // Sits after auth so the diagnostic comment sees the final principal claims;
    // before controllers so it can wrap their response body. The middleware itself
    // is a no-op when env.IsProduction() or when Diagnostics:ShowSessionFooter
    // is false / unset.
    app.UseMiddleware<DiagnosticFooterMiddleware>();

    // Emits `<!-- session: {id} -->` before </body> on every text/html response so
    // users can quote the session id back to support. Placed after auth so any
    // future admin-only variants would still see the right principal; placed after
    // SessionAbsoluteLifetimeMiddleware so Session.Id is stable (cookie committed).
    app.UseMiddleware<SessionSourceCommentMiddleware>();

    app.MapStaticAssets().AllowAnonymous();

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
