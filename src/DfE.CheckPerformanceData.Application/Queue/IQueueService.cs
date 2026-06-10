namespace DfE.CheckPerformanceData.Application.Queue;

public interface IQueueService
{
    Task EnqueueAsync<T>(string queueName, T message, CancellationToken cancellationToken = default);

    Task<QueueMessage?> DequeueAsync(string queueName, CancellationToken cancellationToken = default);

    Task AckAsync(Guid messageId, CancellationToken cancellationToken = default);

    Task DeadLetterAsync(Guid messageId, string reason, CancellationToken cancellationToken = default);
}
