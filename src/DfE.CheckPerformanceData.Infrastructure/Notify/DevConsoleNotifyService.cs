using System.Collections.Generic;
using System.Threading.Tasks;
using DfE.CheckPerformanceData.Application.Notify;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Infrastructure.Notify;

public sealed class DevConsoleNotifyService : INotifyService
{
    private readonly ILogger<DevConsoleNotifyService> _logger;

    public DevConsoleNotifyService(ILogger<DevConsoleNotifyService> logger)
    {
        _logger = logger;
    }

    public Task SendNotificationsAsync(
        string referenceNumber,
        string deadline,
        IReadOnlyCollection<string> recipientEmails,
        NotificationType notificationType,
        string? url = null,
        IReadOnlyCollection<string>? referenceNumbers = null)
    {
        var recipientCount = recipientEmails.Count;
        var recipientList = string.Join(", ", recipientEmails);
        var refs = referenceNumbers is { Count: > 0 } ? string.Join(", ", referenceNumbers) : referenceNumber;

        _logger.LogInformation(
            "[Notify:Fake] Sending {NotificationType} notification\n  Reference(s): {References}\n  Deadline: {Deadline}\n  Recipients: {RecipientCount} — [{RecipientEmails}]{Url}",
            notificationType, refs, deadline, recipientCount, recipientList,
            string.IsNullOrEmpty(url) ? "" : $"\n  URL: {url}");

        return Task.CompletedTask;
    }

    public Task SendDlqThresholdEmailAsync(string toEmail, int dlqDepth, int threshold)
    {
        _logger.LogInformation(
            "[Notify:Fake] DLQ Threshold Alert\n  To: {ToEmail}\n  DLQ Depth: {DlqDepth}\n  Threshold: {Threshold}",
            toEmail, dlqDepth, threshold);

        return Task.CompletedTask;
    }
}
