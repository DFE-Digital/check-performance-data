using Azure.Storage.Queues;
using DfE.CheckPerformanceData.Application;
using DfE.CheckPerformanceData.Application.RequestDecision;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using WorkerService = DfE.CheckPerformanceData.RulesEngineWorker.RulesEngineWorker;
using RulesEngineOptions = DfE.CheckPerformanceData.RulesEngineWorker.RulesEngineOptions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Worker;

public sealed class RulesEngineWorkerCompositionTests
{
    /// <summary>
    /// Mirrors the worker's Program.cs registrations and builds the provider with
    /// the same validation the Development host applies. This is the regression
    /// test for the startup AggregateException: the worker must not drag in
    /// Application services whose repositories/clients only the Web host registers,
    /// and its hosted service must not capture scoped services.
    /// </summary>
    [Fact]
    public void WorkerServiceGraph_BuildsWithFullValidation()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:AzureStorage"] = "UseDevelopmentStorage=true",
            ["ZendeskSettings:Subdomain"] = "test",
            ["ZendeskSettings:Domain"] = "zendesk",
            ["ZendeskSettings:Email"] = "t@example.com",
            ["ZendeskSettings:ApiToken"] = "token",
            ["SchoolCheckingExercise:TargetViewTitle"] = "View",
            ["SchoolCheckingExercise:GroupId"] = "1",
            ["SchoolCheckingExercise:BrandId"] = "1",
            ["RulesEngineOptions:QueueName"] = "test-queue",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Keep in step with src/DfE.CheckPerformanceData.RulesEngineWorker/Program.cs.
        services.Configure<RulesEngineOptions>(config.GetSection("RulesEngineOptions"));
        services.AddSingleton(_ => new QueueServiceClient(config.GetConnectionString("AzureStorage")));
        services.AddZendeskApiClient(config);
        services.AddInfrastructureDependencies(config);
        services.AddRulesEngineDependencies();
        services.AddRulesProvider(config);
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService, WorkerService>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    [Fact]
    public async Task ProcessMessageBody_ResolvesHandlerFromScope_AndInvokesIt()
    {
        var handler = Substitute.For<IRequestDecisionHandler>();
        var rulesProvider = Substitute.For<IRulesProvider>();
        rulesProvider.Current.Returns(new RulesSnapshot(
            new RuleSet("v1", DateTimeOffset.UtcNow, []), Lookups.Empty, "v1", DateTimeOffset.UtcNow, RulesHealth.Healthy));
        var engine = Substitute.For<IRulesEngine>();
        engine.Evaluate(Arg.Any<RuleSet>(), Arg.Any<RuleContext>(), Arg.Any<Lookups>())
            .Returns(new Decision(DecisionStatus.Scrutiny, "k", "r", []));

        var scopedServices = new ServiceCollection();
        scopedServices.AddScoped<IRequestDecisionHandler>(_ => handler);
        using var provider = scopedServices.BuildServiceProvider();

        var queueServiceClient = Substitute.For<QueueServiceClient>();
        queueServiceClient.GetQueueClient(Arg.Any<string>()).Returns(Substitute.For<QueueClient>());

        var worker = new WorkerService(
            Substitute.For<ILogger<WorkerService>>(),
            queueServiceClient,
            Options.Create(new RulesEngineOptions { QueueName = "q", MaxDequeueCount = 1 }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            rulesProvider,
            engine,
            new RuleContextMapper());

        await worker.ProcessMessageBodyAsync(MinimalMessageJson, CancellationToken.None);

        await handler.Received(1).HandleAsync(
            Arg.Any<RequestDocument>(), Arg.Any<Decision>(), Arg.Any<CancellationToken>());
    }

    private const string MinimalMessageJson = """
        {
          "CheckingWindowId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
          "CheckingWindowType": "KS4",
          "RequestTypeCode": "Remove - pupil-died",
          "School": { "Urn": "1", "Name": "S" },
          "SubmittedBy": { "UserId": "u", "DisplayName": "A" },
          "Pupil": { "Id": "p", "CypmdId": "c", "Firstname": "B", "Surname": "S", "DateOfBirth": "01/01/2010", "Sex": "M", "Age": 15, "Upn": "U" },
          "Answers": [],
          "ReferenceNumber": "REF-1",
          "SubmittedAt": "2026-06-10T10:00:00Z"
        }
        """;
}
