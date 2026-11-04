namespace DfE.CheckPerformanceData.Application.ZendeskClient;

/// <summary>
/// Makes the address of a ticket in the Zendesk instance that this environment sends tickets to.
/// The address comes from the Zendesk settings, so a view never holds a Zendesk URL.
/// </summary>
public interface IZendeskTicketLinks
{
    /// <summary>
    /// The agent page of the ticket, or null when there is no real ticket to open. That is when
    /// the CrmId is not a ticket id, when the fake Zendesk client is in use, or when the Zendesk
    /// host settings are not set.
    /// </summary>
    string? TicketUrl(string? crmId);
}
