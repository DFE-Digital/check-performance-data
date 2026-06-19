using DfE.CheckPerformanceData.Application.Observability;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Observability;

// The enqueue-time first step on the journey timeline. Recording is observability, not
// correctness: a sink failure must be logged and swallowed so it can never break the
// submission that just enqueued the message — the same discipline as the consumers'
// RecordMetricSafelyAsync.
public sealed class SubmittedMetricRecorderTests
{
    [Fact]
    public async Task RecordAsync_WritesASubmittedStageMetric()
    {
        var sink = Substitute.For<IMetricsSink>();
        var recorder = new SubmittedMetricRecorder(sink);
        var messageId = Guid.NewGuid();

        await recorder.RecordAsync("rules-engine", "REF-123", messageId, CancellationToken.None);

        await sink.Received(1).RecordAsync(
            Arg.Is<QueueMetricEvent>(m =>
                m.Stage == MetricStages.Submitted &&
                m.QueueName == "rules-engine" &&
                m.ReferenceNumber == "REF-123" &&
                m.MessageId == messageId &&
                m.DecisionStatus == null &&
                m.LatencyMs == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordAsync_SinkFailure_IsSwallowed()
    {
        var sink = Substitute.For<IMetricsSink>();
        sink.RecordAsync(Arg.Any<QueueMetricEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("sink down"));
        var recorder = new SubmittedMetricRecorder(sink);

        // Must not throw: the submission that enqueued the message has already succeeded.
        await recorder.RecordAsync("rules-engine", "REF-456", Guid.NewGuid(), CancellationToken.None);
    }
}
