using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using Microsoft.Extensions.Configuration;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient;

// Links to the same instance that the worker sends tickets to. The web app and the worker read
// one config map, so ZendeskSettings__Subdomain and __Domain name that instance here too.
//
// The fake client writes ticket ids to the dev outbox. They are numbers, but no Zendesk has them,
// so a link would open the wrong ticket or none. The default is the worker's default (fake), so a
// host with no setting shows plain text.
public sealed class ZendeskTicketLinks : IZendeskTicketLinks
{
    private readonly string? _ticketsBaseUrl;

    public ZendeskTicketLinks(IConfiguration configuration)
    {
        if (configuration.GetValue(SettingKeys.ZendeskUseFake, defaultValue: true))
            return;

        var settings = configuration.GetSection(ZendeskSettings.SectionName).Get<ZendeskSettings>();
        if (settings is null || DependencyManager.MissingZendeskHostSettings(settings).Count > 0)
            return;

        _ticketsBaseUrl = $"https://{settings.Subdomain.Trim()}.{settings.Domain.Trim()}.com/agent/tickets/";
    }

    public string? TicketUrl(string? crmId) =>
        _ticketsBaseUrl is not null && long.TryParse(crmId, out var ticketId) && ticketId > 0
            ? $"{_ticketsBaseUrl}{ticketId}"
            : null;
}
