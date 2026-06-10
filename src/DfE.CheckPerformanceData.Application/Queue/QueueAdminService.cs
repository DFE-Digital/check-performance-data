namespace DfE.CheckPerformanceData.Application.Queue;

public sealed record QueueDepth(string QueueName, int Depth, TimeSpan? OldestMessageAge);

public sealed record DlqMessage(
    Guid Id,
    string QueueName,
    int Attempts,
    string Reason,
    string Payload,
    DateTime DeadLetteredAtUtc);

public interface IQueueAdminService
{
    Task<IReadOnlyList<QueueDepth>> GetQueueDepthsAsync(CancellationToken cancellationToken = default);

    Task<int> GetDlqCountAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DlqMessage>> GetDlqMessagesAsync(CancellationToken cancellationToken = default);

    Task<DlqMessage?> GetDlqMessageAsync(Guid id, CancellationToken cancellationToken = default);

    Task RedriveAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken = default);

    Task PurgeAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken = default);

    Task<int> PurgeExpiredAsync(TimeSpan retention, CancellationToken cancellationToken = default);
}

public sealed class QueueAdminService : IQueueAdminService
{
    private readonly IQueueService _queueService;

    public QueueAdminService(IQueueService queueService)
    {
        _queueService = queueService;
    }

    public Task<IReadOnlyList<QueueDepth>> GetQueueDepthsAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<int> GetDlqCountAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<DlqMessage>> GetDlqMessagesAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<DlqMessage?> GetDlqMessageAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task RedriveAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task PurgeAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<int> PurgeExpiredAsync(TimeSpan retention, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
