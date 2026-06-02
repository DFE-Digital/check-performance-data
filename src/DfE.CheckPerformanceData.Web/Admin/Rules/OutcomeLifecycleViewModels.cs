using DfE.CheckPerformanceData.Application.RulesConfig;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

public sealed class AddOutcomeForm
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public sealed class AddOutcomeViewModel
{
    public required AddOutcomeForm Form { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }

    public static AddOutcomeViewModel For(AddOutcomeForm form, IReadOnlyList<string>? errors = null) =>
        new() { Form = form, Errors = errors ?? Array.Empty<string>() };
}

public sealed class DeleteOutcomeViewModel
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required int BranchCount { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

public sealed class RollbackConfirmViewModel
{
    public required RulesConfigType ConfigType { get; init; }
    public required int VersionId { get; init; }
    public required int VersionNumber { get; init; }
    public required DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
}
