namespace DfE.CheckPerformanceData.Application.RulesEngine;

/// <summary>
/// The engine's input: a normalised view of a single request. Built by
/// <see cref="IRuleContextMapper"/> from a queue <c>RequestDocument</c> so the
/// evaluator never has to know the message shape.
/// </summary>
/// <param name="OutcomeKey">
/// Canonical outcome key (e.g. <c>ElectiveHomeEducation</c>), derived from
/// the request's <c>WhatToChange</c> reason. <c>_unknown</c> if no mapping exists.
/// </param>
/// <param name="CheckingWindowType">
/// Canonical checking window type: <c>KS2</c> / <c>KS4June</c> / <c>KS4Autumn</c> /
/// <c>Post16</c>. The docx's <c>"16 to 18"</c> phrasing is normalised to <c>Post16</c>;
/// unrecognised values pass through and match no predicate (Scrutiny via <c>otherwise</c>).
/// </param>
/// <param name="Fields">
/// Map of canonical field name → <see cref="FieldValue"/>. Names referenced by
/// <see cref="Predicate"/>s must exist in <see cref="FieldCatalogue"/>. Missing
/// answers project to <see cref="FieldValue.Unknown"/>.
/// </param>
public sealed record RuleContext(
    string OutcomeKey,
    string CheckingWindowType,
    IReadOnlyDictionary<string, FieldValue> Fields)
{
    public FieldValue GetField(string name) =>
        Fields.TryGetValue(name, out var v) ? v : FieldValue.Unknown.Instance;
}
