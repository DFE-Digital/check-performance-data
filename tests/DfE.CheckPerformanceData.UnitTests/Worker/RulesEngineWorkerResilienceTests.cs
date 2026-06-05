using Azure;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using DfE.CheckPerformanceData.Application.RequestDecision;
using DfE.CheckPerformanceData.Application.RulesEngine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using WorkerService = DfE.CheckPerformanceData.RulesEngineWorker.RulesEngineWorker;
using RulesEngineOptions = DfE.CheckPerformanceData.RulesEngineWorker.RulesEngineOptions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Worker;

public sealed class RulesEngineWorkerResilienceTests
{
    private static RulesEngineOptions FastOptions() => new()
    {
        QueueName = "rules-queue",
        MaxMessagesPerPoll = 1,
        RetryDelayMs = 0,
        EmptyQueueDelayMs = 0,
        MaxDequeueCount = 5,
        VisibilityTimeout = TimeSpan.FromSeconds(30)
    };

    private static WorkerService CreateWorker(QueueServiceClient serviceClient, RulesEngineOptions options) =>
        new(
            Substitute.For<ILogger<WorkerService>>(),
            serviceClient,
            Options.Create(options),
            Substitute.For<IRequestDecisionHandler>(),
            Substitute.For<IRulesProvider>(),
            Substitute.For<IRulesEngine>(),
            Substitute.For<IRuleContextMapper>());

    [Fact]
    public async Task ExecuteAsync_WhenQueueCreationFailsTransiently_RecoversWithoutTearingDownTheHost()
    {
        var queueClient = Substitute.For<QueueClient>();
        var serviceClient = Substitute.For<QueueServiceClient>();
        serviceClient.GetQueueClient(Arg.Any<string>()).Returns(queueClient);

        // First create attempt throws a transient storage error; the second succeeds.
        // The pre-fix code creates the queue once, outside the poll loop, so the
        // transient throw escapes ExecuteAsync and stops the host (CrashLoopBackOff).
        queueClient.CreateIfNotExistsAsync(Arg.Any<IDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new RequestFailedException(503, "storage temporarily unavailable"),
                _ => Task.FromResult<Response>(null!));

        var empty = Response.FromValue(Array.Empty<QueueMessage>(), Substitute.For<Response>());
        queueClient.ReceiveMessagesAsync(Arg.Any<int?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(empty));

        var worker = CreateWorker(serviceClient, FastOptions());

        using var cts = new CancellationTokenSource();

        // Should not throw: a transient startup failure must be retried, not fatal.
        await worker.StartAsync(cts.Token);
        await Task.Delay(150);
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);

        // Proves the worker got past the failed creation and reached the queue —
        // i.e. it recovered rather than crashing on the transient error.
        await queueClient.Received().ReceiveMessagesAsync(
            Arg.Any<int?>(), Arg.Any<TimeSpan?>(), Arg.Any<CancellationToken>());
    }
}
