namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>
/// The classification of a duplicate check over a school-window's pupil population
/// (included + non-included). Derived from the count and inclusion of the matches.
/// </summary>
public enum DuplicateScenario
{
    /// <summary>Zero matches — the Add journey continues unchanged with no duplicate page.</summary>
    None,

    /// <summary>Exactly one match that is not included — warn + Abort + Continue, plus Include.</summary>
    SingleNonIncluded,

    /// <summary>Exactly one match that is already included — warn + Abort + Continue, no Include.</summary>
    SingleIncluded,

    /// <summary>Two or more matches — warn + Abort + Continue, with Switch-to-Include per non-included row.</summary>
    Multiple
}

/// <summary>
/// One matching pupil from a duplicate check. Carries PII for the warning page display only —
/// no caller may write these values to logs, analytics, or error messages.
/// </summary>
public sealed class DuplicateMatch
{
    /// <summary>Pupil identity, used for the Include / Switch-to-Include hand-off.</summary>
    public required Guid Id { get; init; }

    public required string Firstname { get; init; }

    public required string Surname { get; init; }

    /// <summary>Display form (dd/MM/yyyy), as <see cref="PupilDateFormatter"/> renders it.</summary>
    public required string DateOfBirth { get; init; }

    /// <summary>UPN for KS4, ULN for Post16. Display only.</summary>
    public required string Identifier { get; init; }

    /// <summary>Drives whether an Include / Switch-to-Include action is offered for this row.</summary>
    public required bool IsIncluded { get; init; }
}

/// <summary>
/// The outcome of a duplicate check over the pupil population. Named with a <c>Pupil</c> prefix to
/// distinguish it from the <c>RequestSubmission.DuplicateCheckResult</c> used by the conflict
/// validation, which shares the Journey build's namespace imports.
/// </summary>
/// <remarks>
/// When the underlying query fails or times out the result carries no matches
/// (<see cref="DuplicateScenario.None"/>) so the journey continues, and a structured PII-free
/// error is logged by the service.
/// </remarks>
public sealed class PupilDuplicateCheckResult
{
    public required IReadOnlyList<DuplicateMatch> Matches { get; init; }

    public required DuplicateScenario Scenario { get; init; }

    /// <summary>A no-match result, also used for the query-failure fast path.</summary>
    public static PupilDuplicateCheckResult None { get; } = new PupilDuplicateCheckResult
    {
        Matches = [],
        Scenario = DuplicateScenario.None
    };

    /// <summary>Derives the scenario from the match set per the branching rules.</summary>
    public static PupilDuplicateCheckResult Build(IReadOnlyList<DuplicateMatch> matches) => new()
    {
        Matches = matches,
        Scenario = matches.Count switch
        {
            0 => DuplicateScenario.None,
            1 => matches[0].IsIncluded ? DuplicateScenario.SingleIncluded : DuplicateScenario.SingleNonIncluded,
            _ => DuplicateScenario.Multiple
        }
    };
}
