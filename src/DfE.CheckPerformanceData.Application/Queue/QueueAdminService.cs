namespace DfE.CheckPerformanceData.Application.Queue;

public sealed record QueueDepth(string QueueName, int Depth, TimeSpan? OldestMessageAge);

// A lightweight summary of a pending message on a working queue (not the dead-letter queue),
// used for the per-queue top-5 list and the per-queue full-list view. Payloads are not carried
// here — message detail is reached through a redaction-gated detail link, never listed raw.
public sealed record QueueMessageSummary(
    Guid Id,
    string QueueName,
    DateTime EnqueuedAtUtc,
    int Attempts);

// A page of pending messages on a working queue plus the total depth, so the per-queue view-all
// list can render a pager without loading every waiting message into the web process. Rows are
// paged in SQL (Skip/Take) and the total comes from a COUNT.
public sealed record QueueMessagesPage(
    IReadOnlyList<QueueMessageSummary> Messages,
    int TotalCount,
    int Page,
    int PageSize);

// A single pending message including its raw payload, for the redaction-gated detail view.
public sealed record QueueMessageDetail(
    Guid Id,
    string QueueName,
    DateTime EnqueuedAtUtc,
    int Attempts,
    string Payload);

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

    Task<IReadOnlyList<QueueMessageSummary>> GetTopMessagesAsync(string queueName, int count, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QueueMessageSummary>> GetQueueMessagesAsync(string queueName, CancellationToken cancellationToken = default);

    Task<QueueMessagesPage> GetQueueMessagesPageAsync(string queueName, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<QueueMessageDetail?> GetMessageDetailAsync(string queueName, Guid id, CancellationToken cancellationToken = default);

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
        _queueService.GetQueueDepthsAsync(cancellationToken);

    public Task<int> GetDlqCountAsync(CancellationToken cancellationToken = default) =>
        _queueService.GetDlqCountAsync(cancellationToken);

    public Task<IReadOnlyList<DlqMessage>> GetDlqMessagesAsync(CancellationToken cancellationToken = default) =>
        _queueService.GetDlqMessagesAsync(cancellationToken);

    public Task<DlqMessage?> GetDlqMessageAsync(Guid id, CancellationToken cancellationToken = default) =>
        _queueService.GetDlqMessageAsync(id, cancellationToken);

    public Task<IReadOnlyList<QueueMessageSummary>> GetTopMessagesAsync(string queueName, int count, CancellationToken cancellationToken = default) =>
        _queueService.GetTopMessagesAsync(queueName, count, cancellationToken);

    public Task<IReadOnlyList<QueueMessageSummary>> GetQueueMessagesAsync(string queueName, CancellationToken cancellationToken = default) =>
        _queueService.GetQueueMessagesAsync(queueName, cancellationToken);

    public Task<QueueMessagesPage> GetQueueMessagesPageAsync(string queueName, int page, int pageSize, CancellationToken cancellationToken = default) =>
        _queueService.GetQueueMessagesPageAsync(queueName, page, pageSize, cancellationToken);

    public Task<QueueMessageDetail?> GetMessageDetailAsync(string queueName, Guid id, CancellationToken cancellationToken = default) =>
        _queueService.GetMessageDetailAsync(queueName, id, cancellationToken);

    public Task RedriveAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken = default) =>
        _queueService.RedriveAsync(messageIds, cancellationToken);

    public Task PurgeAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken = default) =>
        _queueService.PurgeAsync(messageIds, cancellationToken);

    public Task<int> PurgeExpiredAsync(TimeSpan retention, CancellationToken cancellationToken = default) =>
        _queueService.PurgeExpiredAsync(retention, cancellationToken);
}
