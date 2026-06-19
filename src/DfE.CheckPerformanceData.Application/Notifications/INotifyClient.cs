namespace DfE.CheckPerformanceData.Application.Notifications;

// Sends transactional email via GOV.UK Notify. Personalisation carries operational
// metadata only (for example a queue depth) — never any pupil or person identifiers.
public interface INotifyClient
{
    Task SendEmailAsync(
        string recipient,
        IReadOnlyDictionary<string, object> personalisation,
        CancellationToken cancellationToken = default);
}
