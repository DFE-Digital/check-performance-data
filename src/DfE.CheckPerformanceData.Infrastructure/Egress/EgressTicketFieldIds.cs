using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.Infrastructure.Egress;

/// <summary>
/// Turns the custom fields of one ticket into an <see cref="EgressTicket"/>. Both ticket sources
/// use it, so the real Zendesk and the dev outbox read a ticket the same way.
/// </summary>
internal static class EgressTicketFieldIds
{
    /// <summary>
    /// Gets the configured id of each field in <see cref="EgressTicketFields.All"/>. Egress fails
    /// closed: when one id is not set (null or 0), the pull refuses and names each missing setting.
    /// </summary>
    public static IReadOnlyDictionary<string, long> Resolve(ZendeskTicketFieldSettings settings)
    {
        var ids = EgressTicketFields.All.ToDictionary(name => name, settings.GetFieldIdByName);
        var unset = ids.Where(p => p.Value is not > 0).Select(p => $"\"{p.Key}\"").ToList();
        if (unset.Count > 0)
            throw new EgressTicketSourceException(
                $"These Zendesk field ids are not configured for this environment (ZendeskTicketFields), so egress cannot read them: {string.Join(", ", unset)}.");
        return ids.ToDictionary(p => p.Key, p => p.Value!.Value);
    }

    /// <param name="values">The ticket's custom field values by field id. A blank value counts as no value.</param>
    public static EgressTicket ToTicket(long decisionFieldId, IReadOnlyDictionary<string, long> fieldIds, IReadOnlyDictionary<long, string?> values)
    {
        var decision = Value(values, decisionFieldId)?.ToLowerInvariant();
        var fields = new Dictionary<string, string>();
        foreach (var (name, id) in fieldIds)
        {
            if (Value(values, id) is { } value) fields[name] = value;
        }
        return new EgressTicket(decision, fields);
    }

    private static string? Value(IReadOnlyDictionary<long, string?> values, long id) =>
        values.TryGetValue(id, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
