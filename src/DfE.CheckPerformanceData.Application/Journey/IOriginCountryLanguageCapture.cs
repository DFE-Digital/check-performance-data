namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// Resolves and stores the origin country's ISO code and official languages on the
/// journey when a posted page contains the country-originally-from answer (PBI 292266).
/// The languages come from the same country-languages.json the rules engine's
/// officialLanguageIs predicate uses, so the journey-side auto-reject approximation
/// and the engine read one source of truth.
/// </summary>
public interface IOriginCountryLanguageCapture
{
    Task ApplyAsync(RequestState journey, IReadOnlyDictionary<string, QuestionAnswer> newAnswers, CancellationToken cancellationToken = default);
}
