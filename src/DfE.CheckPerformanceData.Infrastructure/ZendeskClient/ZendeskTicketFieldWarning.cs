using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient;

/// <summary>
/// Reports Zendesk ticket field IDs that are not set, once, as the host starts.
/// </summary>
/// <remarks>
/// A field ID that is not set causes no error. The worker leaves that field out of each ticket,
/// and the ticket reaches Zendesk incomplete. This warning names the settings so that the gap is
/// seen at start, not in a Zendesk report later.
///
/// It is a hosted service because the settings are read during registration, before the host
/// and therefore before any logger exists. See <see cref="ZendeskCredentialWarning"/>.
/// </remarks>
internal sealed class ZendeskTicketFieldWarning(
    IReadOnlyCollection<string> unsetSettings,
    ILogger<ZendeskTicketFieldWarning> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The names only, never the values.
        logger.LogWarning(
            "Zendesk ticket field IDs are not configured: {UnsetSettings}. The real Zendesk client " +
            "is in use, so tickets will not have these fields until the settings are set.",
            string.Join(", ", unsetSettings));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
