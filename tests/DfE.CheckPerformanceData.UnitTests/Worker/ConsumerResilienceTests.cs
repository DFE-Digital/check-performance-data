using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.RulesEngineWorker.Consumers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Worker;

/// <summary>
/// A failure while processing one message must not tear down the consumer host:
/// the dequeue loop logs the error, leaves the message for retry/DLQ, and carries
/// on claiming the next message.
/// </summary>
public sealed class ConsumerResilienceTests
{
    [Fact]
    public async Task ProcessingThrow_DoesNotCrashLoop_AndKeepsPolling()
    {
        var queueService = Substitute.For<IQueueService>();

        var poisonMessage = new QueueMessage
        {
            Id = Guid.NewGuid(),
            QueueName = "test-queue",
            Payload = "poison",
            Attempts = 1,
        };

        // First dequeue hands back a message whose processing throws; every later
        // dequeue returns null (empty queue). A crash on the throw would stop the loop
        // before any further dequeue.
        var dequeueCount = 0;
        queueService.DequeueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<QueueMessage?>(
                Interlocked.Increment(ref dequeueCount) == 1 ? poisonMessage : null));

        var options = Options.Create(new QueueOptions
        {
            RetryDelay = TimeSpan.Zero,
            MaxAttempts = 5,
        });

        var consumer = new ThrowingConsumer(queueService, options);

        using var cts = new CancellationTokenSource();
        await consumer.StartAsync(cts.Token);

        // Give the loop room to process the poison message and poll again.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (dequeueCount < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        await cts.CancelAsync();
        await consumer.StopAsync(CancellationToken.None);

        Assert.True(dequeueCount >= 2, "loop should keep polling after a processing failure");
        // The poison message was below MaxAttempts, so it is left for retry, not acked.
        await queueService.DidNotReceive().AckAsync(poisonMessage.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessingThrow_AtAttemptCap_DeadLettersTheMessage()
    {
        var queueService = Substitute.For<IQueueService>();

        var poisonMessage = new QueueMessage
        {
            Id = Guid.NewGuid(),
            QueueName = "test-queue",
            Payload = "poison",
            Attempts = 5,
        };

        var dequeueCount = 0;
        queueService.DequeueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<QueueMessage?>(
                Interlocked.Increment(ref dequeueCount) == 1 ? poisonMessage : null));

        var options = Options.Create(new QueueOptions
        {
            RetryDelay = TimeSpan.Zero,
            MaxAttempts = 5,
        });

        var consumer = new ThrowingConsumer(queueService, options);

        using var cts = new CancellationTokenSource();
        await consumer.StartAsync(cts.Token);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (dequeueCount < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        await cts.CancelAsync();
        await consumer.StopAsync(CancellationToken.None);

        await queueService.Received().DeadLetterAsync(poisonMessage.Id, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private sealed class ThrowingConsumer : ConsumerBase
    {
        public ThrowingConsumer(IQueueService queueService, IOptions<QueueOptions> options)
            : base(queueService, options, NullLogger<ThrowingConsumer>.Instance)
        {
        }

        protected override string QueueName => "test-queue";

        public override Task ProcessMessageBodyAsync(string messageBody, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }
}
