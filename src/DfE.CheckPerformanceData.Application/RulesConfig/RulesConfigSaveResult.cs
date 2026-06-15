namespace DfE.CheckPerformanceData.Application.RulesConfig;

/// <summary>
/// Outcome of a save attempt. Validation failures carry the error list for a GOV.UK error
/// summary; nothing is persisted in that case.
/// </summary>
public sealed record RulesConfigSaveResult(bool Saved, int? VersionNumber, IReadOnlyList<string> Errors)
{
    public static RulesConfigSaveResult Success(int versionNumber) => new(true, versionNumber, Array.Empty<string>());
    public static RulesConfigSaveResult Invalid(IReadOnlyList<string> errors) => new(false, null, errors);
}
