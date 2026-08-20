using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient;

/// <summary>
/// Reports Zendesk credentials that are not set, once, as the host starts.
/// </summary>
/// <remarks>
/// Only the two settings that form the hostname stop the client being built, so those are a
/// hard failure at registration. A missing <c>Email</c> or <c>ApiToken</c> costs nothing but the
/// Zendesk call itself, and the same process runs the queue consumers and every retention job,
/// none of which talks to Zendesk — so the host starts and this says what is wrong instead.
/// Silence would leave the missing secret to be discovered from a Zendesk 401 hours later.
///
/// It is a hosted service because the settings are read during registration, before the host
/// and therefore before any logger exists.
/// </remarks>
internal sealed class ZendeskCredentialWarning(
    IReadOnlyCollection<string> missingSettings,
    ILogger<ZendeskCredentialWarning> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The names only. A value absent enough to be reported here is not one worth echoing,
        // and the two settings in question are credentials.
        logger.LogWarning(
            "Zendesk credentials are not configured: {MissingSettings}. The real Zendesk client " +
            "is in use, so calls to Zendesk will fail until the corresponding ZendeskSettings__* " +
            "values are set — the client is addressed at an unresolvable host in the meantime. " +
            "Everything else in this process is unaffected.",
            string.Join(", ", missingSettings));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
