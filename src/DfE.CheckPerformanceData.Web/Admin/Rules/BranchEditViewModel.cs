using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

public sealed class BranchEditViewModel
{
    public required BranchEditForm Form { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required IReadOnlyList<string> AllFields { get; init; }
    public required IReadOnlyList<DecisionStatus> Statuses { get; init; }
    public bool ConcurrencyConflict { get; init; }

    public static BranchEditViewModel For(BranchEditForm form, IReadOnlyList<string>? errors = null,
        bool concurrencyConflict = false) => new()
    {
        Form = form,
        Errors = errors ?? Array.Empty<string>(),
        AllFields = LeafEditorOptions.Fields(),
        Statuses = LeafEditorOptions.Statuses(),
        ConcurrencyConflict = concurrencyConflict
    };
}
