using Azure.Storage.Queues;
using DfE.CheckPerformanceData.Application;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.RequestDecision;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Infrastructure;
using DfE.CheckPerformanceData.Persistence;
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
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=cypd;Username=postgres;Password=postgres",
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
        services.AddSingleton<DfE.CheckPerformanceData.Application.CurrentUser.ICurrentUserService,
            DfE.CheckPerformanceData.RulesEngineWorker.WorkerCurrentUserService>();
        services.AddPersistenceDependencies(config);
        services.AddRulesEngineDependencies();
        services.AddRulesProvider(config);
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService, WorkerService>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        // ProcessMessageBodyAsync resolves these per message; ValidateOnBuild
        // does not cover runtime GetRequiredService, so resolve them explicitly.
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRequestDecisionHandler>();
        scope.ServiceProvider.GetRequiredService<IDecisionOutcomeRepository>();
        scope.ServiceProvider.GetRequiredService<IAnalyticsService>();
    }

    [Fact]
    public async Task ProcessMessageBody_ResolvesHandlerFromScope_AndInvokesIt()
    {
        var handler = Substitute.For<IRequestDecisionHandler>();
        var outcomes = Substitute.For<IDecisionOutcomeRepository>();

        var worker = CreateWorker(handler, outcomes, new Decision(DecisionStatus.Scrutiny, "k", "r", []));

        await worker.ProcessMessageBodyAsync(MinimalMessageJson, CancellationToken.None);

        await handler.Received(1).HandleAsync(
            Arg.Any<RequestDocument>(), Arg.Any<Decision>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageBody_EmitsRequestDecisionEvent_WithDecisionMetadata()
    {
        var analytics = Substitute.For<IAnalyticsService>();
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "EAL-1", []);

        var worker = CreateWorker(
            Substitute.For<IRequestDecisionHandler>(),
            Substitute.For<IDecisionOutcomeRepository>(),
            decision,
            analytics);

        await worker.ProcessMessageBodyAsync(MinimalMessageJson, CancellationToken.None);

        // Non-PII decision metadata, sourced from the decision, the rules snapshot,
        // and the parsed document. RequestTypeCode/CheckingWindowType come from
        // MinimalMessageJson; RulesVersion from the stubbed snapshot.
        await analytics.Received(1).TrackAsync(
            Arg.Is<RequestDecisionEvent>(e =>
                e.DecisionStatus == "AutoApproved" &&
                e.OutcomeKey == "Deceased" &&
                e.MatchedRuleId == "EAL-1" &&
                e.RulesVersion == "v1" &&
                e.RequestTypeCode == "Remove - pupil-died" &&
                e.CheckingWindowType == "KS4June" &&
                !e.IsSyntheticFallback),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageBody_MarksDecisionEventSynthetic_WhenRuleIdIsFallback()
    {
        // Fallback decisions (mapper/engine error, unmatched outcome) carry a
        // '_'-prefixed MatchedRuleId; the event flags them so the metric can separate
        // genuine rule outcomes from error fallbacks.
        var analytics = Substitute.For<IAnalyticsService>();
        var fallback = new Decision(DecisionStatus.Scrutiny, "_unknown", "_engine_error", []);

        var worker = CreateWorker(
            Substitute.For<IRequestDecisionHandler>(),
            Substitute.For<IDecisionOutcomeRepository>(),
            fallback,
            analytics);

        await worker.ProcessMessageBodyAsync(MinimalMessageJson, CancellationToken.None);

        await analytics.Received(1).TrackAsync(
            Arg.Is<RequestDecisionEvent>(e => e.IsSyntheticFallback && e.DecisionStatus == "Scrutiny"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageBody_StillInvokesHandler_WhenAnalyticsThrows()
    {
        // Analytics is observability; a sink failure must not fail message processing
        // (which would leave the message on the queue to be retried/poisoned) nor skip
        // the decision handler.
        var analytics = Substitute.For<IAnalyticsService>();
        analytics.TrackAsync(Arg.Any<AnalyticsEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("analytics sink down"));
        var handler = Substitute.For<IRequestDecisionHandler>();

        var worker = CreateWorker(
            handler,
            Substitute.For<IDecisionOutcomeRepository>(),
            new Decision(DecisionStatus.Scrutiny, "k", "r", []),
            analytics);

        await worker.ProcessMessageBodyAsync(MinimalMessageJson, CancellationToken.None);

        await handler.Received(1).HandleAsync(
            Arg.Any<RequestDocument>(), Arg.Any<Decision>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageBody_RecordsOutcomeOnChangeRequest_BeforeInvokingHandler()
    {
        var decision = new Decision(DecisionStatus.AutoApproved, "PupilDied", "rule-1", []);
        var callOrder = new List<string>();
        var handler = Substitute.For<IRequestDecisionHandler>();
        handler.HandleAsync(Arg.Any<RequestDocument>(), Arg.Any<Decision>(), Arg.Any<CancellationToken>())
            .Returns(_ => { callOrder.Add("handler"); return Task.CompletedTask; });
        var outcomes = Substitute.For<IDecisionOutcomeRepository>();
        outcomes.RecordOutcomeAsync(Arg.Any<Guid>(), Arg.Any<Decision>(), Arg.Any<CancellationToken>())
            .Returns(_ => { callOrder.Add("outcome"); return true; });

        var worker = CreateWorker(handler, outcomes, decision);

        await worker.ProcessMessageBodyAsync(MinimalMessageJson, CancellationToken.None);

        // The decision must be on the row even if Zendesk is down, so the DB
        // write comes first; ChangeRequestId comes from the parsed document.
        Assert.Equal(["outcome", "handler"], callOrder);
        await outcomes.Received(1).RecordOutcomeAsync(
            Guid.Parse("11111111-2222-3333-4444-555555555555"), decision, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessMessageBody_WhenNoChangeRequestRowMatches_StillInvokesHandler()
    {
        var handler = Substitute.For<IRequestDecisionHandler>();
        var outcomes = Substitute.For<IDecisionOutcomeRepository>();
        outcomes.RecordOutcomeAsync(Arg.Any<Guid>(), Arg.Any<Decision>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var worker = CreateWorker(handler, outcomes, new Decision(DecisionStatus.Scrutiny, "k", "r", []));

        await worker.ProcessMessageBodyAsync(MinimalMessageJson, CancellationToken.None);

        await handler.Received(1).HandleAsync(
            Arg.Any<RequestDocument>(), Arg.Any<Decision>(), Arg.Any<CancellationToken>());
    }

    private static WorkerService CreateWorker(
        IRequestDecisionHandler handler, IDecisionOutcomeRepository outcomes, Decision decision,
        IAnalyticsService? analytics = null)
    {
        analytics ??= Substitute.For<IAnalyticsService>();

        var rulesProvider = Substitute.For<IRulesProvider>();
        rulesProvider.Current.Returns(new RulesSnapshot(
            new RuleSet("v1", DateTimeOffset.UtcNow, []), Lookups.Empty, "v1", DateTimeOffset.UtcNow, RulesHealth.Healthy));
        var engine = Substitute.For<IRulesEngine>();
        engine.Evaluate(Arg.Any<RuleSet>(), Arg.Any<RuleContext>(), Arg.Any<Lookups>())
            .Returns(decision);

        var scopedServices = new ServiceCollection();
        scopedServices.AddScoped<IRequestDecisionHandler>(_ => handler);
        scopedServices.AddScoped<IDecisionOutcomeRepository>(_ => outcomes);
        scopedServices.AddScoped<IAnalyticsService>(_ => analytics);
        var provider = scopedServices.BuildServiceProvider();

        var queueServiceClient = Substitute.For<QueueServiceClient>();
        queueServiceClient.GetQueueClient(Arg.Any<string>()).Returns(Substitute.For<QueueClient>());

        return new WorkerService(
            Substitute.For<ILogger<WorkerService>>(),
            queueServiceClient,
            Options.Create(new RulesEngineOptions { QueueName = "q", MaxDequeueCount = 1 }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            rulesProvider,
            engine,
            new RuleContextMapper());
    }

    private const string MinimalMessageJson = """
        {
          "ChangeRequestId": "11111111-2222-3333-4444-555555555555",
          "CheckingWindowId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
          "CheckingWindowType": "KS4June",
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
