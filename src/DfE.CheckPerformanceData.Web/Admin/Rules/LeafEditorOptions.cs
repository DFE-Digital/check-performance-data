using DfE.CheckPerformanceData.Application.RulesEngine;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

public enum ValueEditorKind { Text, Number, Date, BoolSelect, List, Language, None }

public sealed record OperatorOption(string Token, string Label);

/// <summary>
/// Pure dropdown data for the leaf editor: which operators a field's type allows,
/// the value-editor shape per operator, the field list and status list.
/// </summary>
public static class LeafEditorOptions
{
    public static IReadOnlyList<string> Fields() => FieldCatalogue.All.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    public static IReadOnlyList<OperatorOption> OperatorTokensFor(string field)
    {
        if (!FieldCatalogue.TryGetType(field, out var type)) return Array.Empty<OperatorOption>();

        return type switch
        {
            FieldType.String => new[]
            {
                new OperatorOption("eq", "equals"),
                new OperatorOption("neq", "does not equal"),
                new OperatorOption("in", "is one of"),
                new OperatorOption("known", "is known and certain"),
                new OperatorOption("lang", "official language is"),
            },
            FieldType.Number => new[]
            {
                new OperatorOption("lt", "is less than"),
                new OperatorOption("lte", "is less than or equal to"),
                new OperatorOption("gt", "is greater than"),
                new OperatorOption("gte", "is greater than or equal to"),
                new OperatorOption("eq", "equals"),
                new OperatorOption("known", "is known and certain"),
            },
            FieldType.Date => new[]
            {
                new OperatorOption("lt", "is before"),
                new OperatorOption("lte", "is on or before"),
                new OperatorOption("gt", "is after"),
                new OperatorOption("gte", "is on or after"),
                new OperatorOption("eq", "equals"),
            },
            FieldType.Bool => new[]
            {
                new OperatorOption("eq", "equals"),
                new OperatorOption("neq", "does not equal"),
                new OperatorOption("known", "is known and certain"),
            },
            _ => Array.Empty<OperatorOption>()
        };
    }

    public static ValueEditorKind ValueEditor(string? field, string? op) => op switch
    {
        "known" => ValueEditorKind.None,
        "in" => ValueEditorKind.List,
        "lang" => ValueEditorKind.Language,
        _ when field is not null && FieldCatalogue.TryGetType(field, out var t) => t switch
        {
            FieldType.Bool => ValueEditorKind.BoolSelect,
            FieldType.Number => ValueEditorKind.Number,
            FieldType.Date => ValueEditorKind.Date,
            _ => ValueEditorKind.Text
        },
        _ => ValueEditorKind.Text
    };

    public static IReadOnlyList<DecisionStatus> Statuses() =>
        new[] { DecisionStatus.AutoApproved, DecisionStatus.AutoRejected, DecisionStatus.Scrutiny };
}
