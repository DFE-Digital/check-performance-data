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
    IReadOnlyList<(string QuestionId, string Value)> Answers,
    int Pincl = 0);

internal static class OutcomePresets
{
    // Inclusion routes on the pupil's Pincl (402 = added back into the cohort), not on a
    // journey answer, so the approved preset carries it on the pupil record and no answers.
    private static readonly OutcomePreset Approved = new(
        Name: "approved",
        ExpectedDecision: "AutoApproved",
        WhatToChange: "Include",
        CheckingWindowType: "KS4June",
        PupilAge: 12,
        Answers: Array.Empty<(string, string)>(),
        Pincl: 402);

    private static readonly OutcomePreset Rejected = new(
        Name: "rejected",
        ExpectedDecision: "AutoRejected",
        WhatToChange: "Remove - english-not-first-language",
        CheckingWindowType: "KS4June",
        PupilAge: 12,
        Answers: new[] { ("first-language", "english") });

    private static readonly OutcomePreset Scrutiny = new(
        Name: "scrutiny",
        ExpectedDecision: "Scrutiny",
        WhatToChange: "Merge",
        CheckingWindowType: "KS4June",
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
