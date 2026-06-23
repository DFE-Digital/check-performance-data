using System.Collections.Generic;
using System.Threading.Tasks;

namespace DfE.CheckPerformanceData.Application.Notify;

public interface INotifyEmailClient
{
    Task SendEmailAsync(string email, string templateId, Dictionary<string, object>? personalisation = null);
}
