namespace DfE.CheckPerformanceData.Application.Queue;

public interface IQueueService
{
    // Returns the id of the enqueued row so a caller can address that specific message later
    // (e.g. dead-letter it) without dequeuing, which would claim the oldest visible message.
    Task<Guid> EnqueueAsync<T>(string queueName, T message, CancellationToken cancellationToken = default);

    Task<QueueMessage?> DequeueAsync(string queueName, CancellationToken cancellationToken = default);

    Task AckAsync(Guid messageId, CancellationToken cancellationToken = default);

    Task DeadLetterAsync(Guid messageId, string reason, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QueueDepth>> GetQueueDepthsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DlqMessage>> GetDlqMessagesAsync(CancellationToken cancellationToken = default);

    Task<DlqMessage?> GetDlqMessageAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QueueMessageSummary>> GetTopMessagesAsync(string queueName, int count, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QueueMessageSummary>> GetQueueMessagesAsync(string queueName, CancellationToken cancellationToken = default);

    Task<QueueMessageDetail?> GetMessageDetailAsync(string queueName, Guid id, CancellationToken cancellationToken = default);

    Task RedriveAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken = default);

    Task PurgeAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken = default);

    Task<int> PurgeExpiredAsync(TimeSpan retention, CancellationToken cancellationToken = default);
}
