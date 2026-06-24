using System.Collections.Generic;
using System.Threading.Tasks;
using DfE.CheckPerformanceData.Application.Notify;
using Notify.Client;

namespace DfE.CheckPerformanceData.Infrastructure.Notify;

public sealed class NotifyEmailClient : INotifyEmailClient
{
    private readonly NotificationClient _inner;

    public NotifyEmailClient(NotificationClient inner)
    {
        _inner = inner;
    }

    public Task SendEmailAsync(string email, string templateId, Dictionary<string, object>? personalisation = null)
    {
        return _inner.SendEmailAsync(email, templateId, personalisation: personalisation);
    }
}
