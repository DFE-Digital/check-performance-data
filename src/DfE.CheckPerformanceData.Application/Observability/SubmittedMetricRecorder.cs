using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DfE.CheckPerformanceData.Application.Observability;

// Records the Submitted stage — the journey timeline's first step — when the web side enqueues
// a request onto the rules-engine queue. Observability is not correctness: any sink failure is
// logged and swallowed so it can never break the submission that just enqueued the message,
// mirroring the consumers' RecordMetricSafelyAsync discipline. Latency is zero by definition:
// the message has only just been enqueued, so there is no dwell yet.
public sealed class SubmittedMetricRecorder
{
    private readonly IMetricsSink _sink;
    private readonly ILogger<SubmittedMetricRecorder> _logger;

    public SubmittedMetricRecorder(IMetricsSink sink, ILogger<SubmittedMetricRecorder>? logger = null)
    {
        _sink = sink;
        _logger = logger ?? NullLogger<SubmittedMetricRecorder>.Instance;
    }

    public async Task RecordAsync(
        string queueName,
        string referenceNumber,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _sink.RecordAsync(new QueueMetricEvent(
                QueueName: queueName,
                Stage: MetricStages.Submitted,
                ReferenceNumber: referenceNumber,
                MessageId: messageId,
                DecisionStatus: null,
                RulesVersion: null,
                LatencyMs: 0,
                RecordedAtUtc: DateTime.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Metrics sink failed recording the submitted step for {Reference}; continuing.",
                referenceNumber);
        }
    }
}
