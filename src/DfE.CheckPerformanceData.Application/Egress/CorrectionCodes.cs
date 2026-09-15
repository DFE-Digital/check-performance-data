using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// The LDS correction reason for a Remove request is the numeric part of the Zendesk
/// "Correction reason (31)" option the journey's reason maps to ("4_31" → "4"). The Zendesk map
/// is the business-confirmed source (see ZendeskTicketFieldOptions.CorrectionReason31); the two
/// Post16 reasons that mean the same as a KS4 reason reuse its code. Anything else is null — a
/// missing code fails the record, it is never guessed.
/// </summary>
public static class CorrectionCodes
{
    private static readonly IReadOnlyDictionary<string, string> Post16Reasons =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["student-died"] = "4",   // same reason as KS4 pupil-died (4_31)
            ["not-on-roll"] = "6"     // same reason as KS4 not-on-roll (6_31)
        };

    public static string? RemoveReasonCode(string? reasonValue)
    {
        if (string.IsNullOrWhiteSpace(reasonValue))
            return null;

        var zendesk = ZendeskTicketFieldOptions.GetOptionValue(
            ZendeskTicketFieldConstants.CorrectionReason31Name, reasonValue.Trim());
        if (zendesk is not null)
        {
            var suffix = zendesk.LastIndexOf("_31", StringComparison.Ordinal);
            return suffix > 0 ? zendesk[..suffix] : zendesk;
        }

        return Post16Reasons.TryGetValue(reasonValue.Trim(), out var code) ? code : null;
    }
}
