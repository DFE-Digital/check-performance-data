using Azure.Storage.Queues;
using DfE.CheckPerformanceData.Application;
using DfE.CheckPerformanceData.Application.ClaimsEnrichment;
using DfE.CheckPerformanceData.Infrastructure.DfeSignIn;
using DfE.CheckPerformanceData.Infrastructure.DfeSignInApiClient;
using DfE.CheckPerformanceData.Infrastructure.Persistence;
using DfE.CheckPerformanceData.Infrastructure.Seeding;
using DfE.CheckPerformanceData.Web.Extensions;
using GovUk.Frontend.AspNetCore;
using Microsoft.EntityFrameworkCore;
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

    builder.Host.UseSerilog((context, services, configuration) =>
    {
        var isDevelopment = context.HostingEnvironment.IsDevelopment();

        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console(isDevelopment
                ? new ExpressionTemplate(
                    "[{@t:HH:mm:ss} {@l:u3}] {SourceContext}\n  {@m}\n{@x}",
                    theme: TemplateTheme.Code)
                : new CompactJsonFormatter());
    });

    builder.Services
        .AddDfeApiClient(builder.Configuration)
        .AddDfeSignInAuthentication(builder.Configuration)
        .AddGovUkFrontend(options => options.Rebrand = true);

    if (builder.Environment.IsDevelopment()) 
        builder.Services.AddScoped<DevDataSeeder>();
    
    builder.Services.AddScoped<IClaimsEnrichmentService, ClaimsEnrichmentService>();
    
    builder.Services.AddDbContext<PortalDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

    builder.Services.AddScoped<IPortalDbContext>(sp => sp.GetRequiredService<PortalDbContext>());

    builder.Services.AddSingleton(_ => new QueueServiceClient(builder.Configuration.GetConnectionString("AzureStorage"),
        new QueueClientOptions(QueueClientOptions.ServiceVersion.V2025_11_05)
        {
            MessageEncoding = QueueMessageEncoding.Base64
        }));

    builder.Services.AddControllersWithViews();

    builder.Services.AddHealthChecks();

    builder.Services.AddMediatR(config => config.RegisterServicesFromAssembly(typeof(IPortalDbContext).Assembly));
    
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
    // else
    // {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DevDataSeeder>().SeedAsync();
    //}

    app.UseHttpsRedirection();
    app.UseRouting();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapStaticAssets();

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