using System.Threading;
using System.Threading.Tasks;

namespace DfE.CheckPerformanceData.Application.Notify;

/// <summary>
/// The consumer side of <see cref="EmailNotification"/>: resolves recipients and sends the
/// email. Runs off the request thread (invoked by the background worker), so it may perform
/// the slow external calls (DfE Sign-in recipient lookup and the GOV.UK Notify sends).
/// </summary>
public interface INotificationSender
{
    Task SendAsync(EmailNotification notification, CancellationToken cancellationToken = default);
}
