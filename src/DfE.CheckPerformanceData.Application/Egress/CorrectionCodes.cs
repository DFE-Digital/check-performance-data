using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// The LDS correction reason (Correction_Reason) for a Remove request.
/// KS2/KS4: the numeric part of the Zendesk "Correction reason (31)" option the journey's reason
/// maps to ("4_31" → "4") — ZendeskTicketFieldOptions.CorrectionReason31 is the business-confirmed
/// source. 16-19: the codes listed in LDS_CYPMD_Data specification v2.4 "Remove Learner" K11,
/// keyed on the Post16 journey's reason and, for not-on-roll, its not-on-roll-reason. Anything
/// else is null — a missing code fails the record, it is never guessed.
/// </summary>
public static class CorrectionCodes
{
    public static string? RemoveReasonCode(CheckingWindowType windowType, string? reasonValue, string? notOnRollReason)
    {
        if (string.IsNullOrWhiteSpace(reasonValue))
            return null;
        var reason = reasonValue.Trim();

        if (windowType == CheckingWindowType.Post16)
            return Post16Code(reason, notOnRollReason?.Trim());

        var zendesk = ZendeskTicketFieldOptions.GetOptionValue(ZendeskTicketFieldConstants.CorrectionReason31Name, reason);
        if (zendesk is null) return null;
        var suffix = zendesk.LastIndexOf("_31", StringComparison.Ordinal);
        return suffix > 0 ? zendesk[..suffix] : zendesk;
    }

    // v2.4 K11: 4 Deceased; 325 Not at end of 16-18 study; 326 Not on Roll (International Student);
    // 328 Not on Roll (External); 331 Not on Roll (Apprentice); 329 Other - with evidence /
    // Not on Roll (Other - with evidence); 330 Other - evidence not required. CYPMD's Post16 "other"
    // routes always collect evidence, so 330 is never produced here (FLAGGED in the PR notes).
    private static string? Post16Code(string reason, string? notOnRollReason) => reason.ToLowerInvariant() switch
    {
        "student-died" => "4",
        "not-at-end-of-16-19-study" => "325",
        "other" => "329",
        "not-on-roll" => notOnRollReason?.ToLowerInvariant() switch
        {
            "apprentice" => "331",
            "external-candidate" => "328",
            "international-student" => "326",
            "other" => "329",
            _ => null
        },
        _ => null
    };
}
