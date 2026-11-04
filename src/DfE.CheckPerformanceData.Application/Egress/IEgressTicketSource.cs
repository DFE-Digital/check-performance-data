using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// Reads tickets from Zendesk for egress. The values on the ticket are the truth: an agent can
/// change them on a scrutiny ticket, so egress reads them and does not work them out again.
/// </summary>
public interface IEgressTicketSource
{
    /// <summary>
    /// Gets each ticket by id. A ticket that Zendesk did not return is absent.
    /// </summary>
    /// <exception cref="EgressTicketSourceException">A field is not configured for this environment or Zendesk could not be read.</exception>
    Task<IReadOnlyDictionary<long, EgressTicket>> GetTicketsAsync(IReadOnlyCollection<long> ticketIds, CancellationToken ct);
}

/// <summary>
/// One ticket as egress reads it. <see cref="Decision"/> is the "Decision status" value in lower
/// case, or null when the ticket has none. <see cref="Fields"/> holds the value of each field in
/// <see cref="EgressTicketFields.All"/> that the ticket has, keyed by field name.
/// </summary>
public sealed record EgressTicket(string? Decision, IReadOnlyDictionary<string, string> Fields);

/// <summary>
/// The Zendesk ticket fields that egress reads, by name (<see cref="ZendeskTicketFieldConstants"/>).
/// A ticket source refuses to pull when one of them has no field id.
/// </summary>
public static class EgressTicketFields
{
    public static readonly IReadOnlyList<string> All =
    [
        ZendeskTicketFieldConstants.CorrectionTypeName,
        ZendeskTicketFieldConstants.CorrectionReason31Name,
        ZendeskTicketFieldConstants.KeyStageName,
        ZendeskTicketFieldConstants.DfeEstablishmentNumberName,
        ZendeskTicketFieldConstants.SurnameCypmdName,
        ZendeskTicketFieldConstants.ForenameCypmdName,
        ZendeskTicketFieldConstants.SexName,
        ZendeskTicketFieldConstants.DateOfBirthCypmdName,
        ZendeskTicketFieldConstants.AdmissionDateName,
        ZendeskTicketFieldConstants.CycleYearName,
        ZendeskTicketFieldConstants.CycleMonthName,
        ZendeskTicketFieldConstants.SchoolUrnName,
        ZendeskTicketFieldConstants.UpnName,
        ZendeskTicketFieldConstants.LdsMatchedPupilIdName,
        ZendeskTicketFieldConstants.PupilYearGroupCypmdName
    ];
}

public sealed class EgressTicketSourceException(string message, Exception? inner = null) : Exception(message, inner);
