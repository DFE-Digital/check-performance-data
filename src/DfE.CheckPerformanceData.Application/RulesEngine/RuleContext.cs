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
/// <param name="PupilDataStartDate">
/// The window's PupilData exercise start date (#800), the anchor for
/// window-relative date literals. Anchoring on it (not the window's start
/// date) keeps the year fixed when an earlier data-share exercise moves the
/// window's earliest start date. Null when the window has no PupilData
/// exercise; a relative literal then resolves to no date and the engine
/// makes no decision on it.
/// </param>
public sealed record RuleContext(
    string OutcomeKey,
    string CheckingWindowType,
    IReadOnlyDictionary<string, FieldValue> Fields,
    DateOnly? PupilDataStartDate = null)
{
    public FieldValue GetField(string name) =>
        Fields.TryGetValue(name, out var v) ? v : FieldValue.Unknown.Instance;
}
