using Azure.Storage.Queues;
using DfE.CheckPerformanceData.Application;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Infrastructure;
using DfE.CheckPerformanceData.Web.Services;
using DfE.CheckPerformanceData.Persistence;
using DfE.CheckPerformanceData.Persistence.Seeding;
using DfE.CheckPerformanceData.Web.Extensions;
using DfE.CheckPerformanceData.ZendeskClient.Refit;

using GovUk.Frontend.AspNetCore;
using Refit;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Templates;
using Serilog.Templates.Themes;
using System.Net.Http.Headers;
using System.Text;
using DfE.CheckPerformanceData.Application.ZendeskApi;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;


Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting application");

    var builder = WebApplication.CreateBuilder(args);

    var configuration = builder.Configuration
        .SetBasePath(builder.Environment.ContentRootPath)
        .AddJsonFile("appsettings.json", false, true)
        .AddEnvironmentVariables()
        .Build();

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
    
    builder.Services.AddHttpContextAccessor();

    builder.Services
        .AddDfeApiClient(builder.Configuration)
        .AddZendeskApiClient(builder.Configuration)
        .AddDfeSignInAuthentication(builder.Configuration)
        .AddGovUkFrontend();
    
    builder.Services.AddPersistenceDependencies(configuration, builder.Environment.IsDevelopment());
    builder.Services.AddApplicationDependencies();
    builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

    builder.Services.AddSingleton(_ => new QueueServiceClient(builder.Configuration.GetConnectionString("AzureStorage"),
        new QueueClientOptions(QueueClientOptions.ServiceVersion.V2025_11_05)
        {
            MessageEncoding = QueueMessageEncoding.Base64
        }));

    
    //var zendeskSubdomain =  Environment.GetEnvironmentVariable("ZENDESK_SUBDOMAIN") ?? builder.Configuration["ZENDESK_SUBDOMAIN"] ?? "example";
    //var zendeskDomain = builder.Configuration["ZENDESK_DOMAIN"] ?? "domain";
    //var zendeskEmail = builder.Configuration["ZENDESK_EMAIL"];
    //var zendeskApiToken = builder.Configuration["ZENDESK_API_TOKEN"];
    
    //builder.Services.AddTransient<RefitLoggingHandler>();


    //builder.Services.AddRefitClient<IZendeskApi>(new RefitSettings
    //{
    //    ContentSerializer = new NewtonsoftJsonContentSerializer()
    //})

    //   .ConfigureHttpClient(c =>
    //   {
    //       c.BaseAddress = new Uri($"https://{zendeskSubdomain}.{zendeskDomain}.com");
    //       var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{zendeskEmail}/token:{zendeskApiToken}"));
    //       c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
    //       c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    //   })
    //   .AddHttpMessageHandler<RefitLoggingHandler>();
    

    
    builder.Services.AddAntiforgery(options =>
    {
        options.HeaderName = "X-XSRF-TOKEN";
    });

    builder.Services.AddControllersWithViews();

    builder.Services.AddHealthChecks();

    var app = builder.Build();

    await app.MigrateDatabaseAsync();

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

    app.UseHealthChecks("/healthcheck");

// Configure the HTTP request pipeline.
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }
    else
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DevDataSeeder>().SeedAsync();
    }

    app.UseHttpsRedirection();

    app.Use(async (context, next) =>
    {
        context.Response.Headers.Append("Content-Security-Policy",
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdnjs.cloudflare.com; " +
            "style-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com; " +
            "img-src 'self' data: blob:; " +
            "font-src 'self' data: https://cdnjs.cloudflare.com; " +
            "connect-src 'self'; " +
            "frame-src 'self'; " +
            "object-src 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'");
        await next();
    });

    app.UseRouting();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapStaticAssets();

    app.MapControllerRoute(
        name: "wiki",
        pattern: "help/{**slugPath}",
        defaults: new { controller = "Help", action = "Index" });

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