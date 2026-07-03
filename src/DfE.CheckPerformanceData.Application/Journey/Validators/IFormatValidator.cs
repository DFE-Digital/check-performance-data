namespace DfE.CheckPerformanceData.Application.Journey.Validators;

/// <summary>
/// A named format rule for a text question answer. A question references a
/// validator by its <see cref="Name"/> via the config <c>validator</c> field,
/// mirroring how a Radio option names a <c>visibleWhen</c> condition. The rule
/// (pattern + error copy) lives here so it is reusable across configs and
/// testable in isolation. Registered as <see cref="IFormatValidator"/> in the
/// Application <c>DependencyManager</c>.
/// </summary>
public interface IFormatValidator
{
    /// <summary>The name referenced by a question's <c>validator</c> field.</summary>
    string Name { get; }

    /// <summary>True when <paramref name="value"/> satisfies the format rule.</summary>
    bool IsValid(string value);

    /// <summary>GOV.UK-style error shown when the format rule is not satisfied.</summary>
    string FailureMessage { get; }
}
