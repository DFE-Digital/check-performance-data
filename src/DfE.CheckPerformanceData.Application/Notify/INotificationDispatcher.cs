using System.Threading;
using System.Threading.Tasks;

namespace DfE.CheckPerformanceData.Application.Notify;

/// <summary>
/// The seam between the request thread and email sending. Callers enqueue an
/// <see cref="EmailNotification"/> and return immediately; an implementation delivers it to
/// a background worker for sending. Today this is an in-process channel; a queue-backed
/// implementation can replace it later without changing callers.
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>Enqueue a notification for background sending. Must not block on external I/O.</summary>
    ValueTask EnqueueAsync(EmailNotification notification, CancellationToken cancellationToken = default);
}
