using System.ComponentModel.DataAnnotations;
using System.Reflection;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Notify;

/// <summary>
/// Immutable carrier for the three checking-exercise-specific email substitution
/// values (<c>ce name</c>, <c>learner noun</c>, <c>turnaround commitment</c>).
/// Built by callers from the checking window they already hold, then passed through
/// <see cref="IRequestNotificationService"/> → <see cref="EmailNotification"/> →
/// <see cref="INotifyService"/>. Not persisted.
/// </summary>
public sealed record EmailSubstitutions(string CeName, string LearnerNoun, string TurnaroundCommitment)
{
    /// <summary>
    /// Derives the three substitution values from a checking window.
    /// CeName: window.Title, falling back to the [Display(Name)] of CheckingWindowType
    ///         when Title is null/whitespace.
    /// LearnerNoun: "Student" when KeyStage is Post16, else "Pupil".
    /// TurnaroundCommitment: window.TurnaroundCommitment (may be empty; empty means the
    ///         personalisation key is omitted, per FR-006).
    /// </summary>
    public static EmailSubstitutions From(CheckingWindowDto window)
    {
        var ceName = string.IsNullOrWhiteSpace(window.Title)
            ? DisplayNameOf(window.CheckingWindowType)
            : window.Title;

        var learnerNoun = window.KeyStage == KeyStages.Post16 ? "Student" : "Pupil";

        return new EmailSubstitutions(ceName, learnerNoun, window.TurnaroundCommitment);
    }

    private static string DisplayNameOf(CheckingWindowType type)
    {
        var attribute = type.GetType().GetField(type.ToString())
            ?.GetCustomAttribute<DisplayAttribute>();
        return attribute?.Name ?? type.ToString();
    }
}
