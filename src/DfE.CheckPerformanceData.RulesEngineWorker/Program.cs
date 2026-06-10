using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Infrastructure;
using DfE.CheckPerformanceData.Infrastructure.Queue;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Repositories;
using DfE.CheckPerformanceData.RulesEngineWorker;
using DfE.CheckPerformanceData.RulesEngineWorker.Consumers;
using DfE.CheckPerformanceData.RulesEngineWorker.Health;
using DfE.CheckPerformanceData.RulesEngineWorker.Maintenance;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddUserSecrets<Program>();

builder.Services.Configure<QueueOptions>(builder.Configuration.GetSection("QueueOptions"));
builder.Services.AddScoped<IQueueService, PostgresQueueService>();
builder.Services.AddScoped<IQueueAdminService, QueueAdminService>();

builder.Services.AddScoped<ISettingRepository, SettingRepository>();
builder.Services.AddScoped<ISettingService, SettingService>();

builder.Services.AddSingleton<ICurrentUserService, WorkerCurrentUserService>();
builder.Services.AddDbContext<PortalDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"),
        sql => sql.EnableRetryOnFailure()));
builder.Services.AddScoped<IPortalDbContext>(sp => sp.GetRequiredService<PortalDbContext>());

builder.Services.AddZendeskApiClient(builder.Configuration);
builder.Services.AddInfrastructureDependencies(builder.Configuration);

// The worker only needs the rules-engine pieces from the Application layer, not the
// portal's full service graph (which depends on web-only collaborators).
builder.Services.AddSingleton<IRulesEngine, RulesEngine>();
builder.Services.AddSingleton<IRuleContextMapper, RuleContextMapper>();
builder.Services.AddSingleton<RuleSetValidator>();

builder.Services.AddRulesProvider(builder.Configuration);

builder.Services.AddHostedService<RulesConsumer>();
builder.Services.AddHostedService<ZendeskConsumer>();
builder.Services.AddHostedService(sp =>
    new DlqRetentionJob(
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<ILogger<DlqRetentionJob>>()));

builder.Services.AddWorkerHealthChecks();

var app = builder.Build();
app.MapWorkerHealthChecks();
app.Run();
