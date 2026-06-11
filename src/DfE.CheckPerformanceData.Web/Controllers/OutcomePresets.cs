namespace DfE.CheckPerformanceData.Web.Controllers;

// Canned synthetic requests for the dev pipeline trigger, each crafted to route to a known
// rule outcome under the seeded rules.json. Mirrors verified scenarios from the rules-engine
// end-to-end coverage so the dev harness can demonstrate a real approve/reject/scrutiny
// decision rather than always falling through to Scrutiny.
internal sealed record OutcomePreset(
    string Name,
    string ExpectedDecision,
    string WhatToChange,
    string CheckingWindowType,
    int PupilAge,
    IReadOnlyList<(string QuestionId, string Value)> Answers);

internal static class OutcomePresets
{
    private static readonly OutcomePreset Approved = new(
        Name: "approved",
        ExpectedDecision: "AutoApproved",
        WhatToChange: "Inclusion",
        CheckingWindowType: "KS4",
        PupilAge: 12,
        Answers: new[] { ("inclusion-status-flag", "402") });

    private static readonly OutcomePreset Rejected = new(
        Name: "rejected",
        ExpectedDecision: "AutoRejected",
        WhatToChange: "Admitted from abroad with English not first language",
        CheckingWindowType: "KS4",
        PupilAge: 12,
        Answers: new[] { ("first-language", "ENG") });

    private static readonly OutcomePreset Scrutiny = new(
        Name: "scrutiny",
        ExpectedDecision: "Scrutiny",
        WhatToChange: "Other",
        CheckingWindowType: "KS4",
        PupilAge: 12,
        Answers: Array.Empty<(string, string)>());

    // Defaults to the approved preset so the bare trigger demonstrates a real, non-Scrutiny
    // outcome; an unrecognised value falls back the same way.
    public static OutcomePreset Resolve(string? outcome) =>
        (outcome?.Trim().ToLowerInvariant()) switch
        {
            "rejected" => Rejected,
            "scrutiny" => Scrutiny,
            _ => Approved,
        };
}
