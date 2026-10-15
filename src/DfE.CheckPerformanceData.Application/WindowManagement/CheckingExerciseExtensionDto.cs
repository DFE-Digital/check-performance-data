using DfE.CheckPerformanceData.Application.Dashboard;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// AB#307773: one extension of a checking exercise — the schools it names and their later end
/// date. Whether it is open is asked of <see cref="ICheckingExerciseService"/>, never read here.
/// </summary>
public sealed class CheckingExerciseExtensionDto
{
    public Guid Id { get; init; }

    /// <summary>A UK wall-clock value, like every exercise date. Inclusive.</summary>
    public required DateTime EndDate { get; init; }

    /// <summary>Digits only (9334290), the form every blob name and <see cref="LaestabNormaliser"/> use.</summary>
    public IReadOnlyList<string> Laestabs { get; init; } = [];

    public DateTime CreatedAt { get; init; }
    public string CreatedBy { get; init; } = string.Empty;

    /// <summary>First sweep of this extension's requests after it closed; null until then.</summary>
    public DateTime? RequestsSentAt { get; init; }

    /// <summary>The edit guard: what the edit page shows and posts back. See the entity.</summary>
    public int Version { get; init; }

    public int SchoolCount => Laestabs.Count;

    /// <summary>Whether this school is named, in either LAESTAB form. Empty or null is never named.</summary>
    public bool Contains(string? laestab)
    {
        var digits = LaestabNormaliser.Normalise(laestab);
        return digits.Length > 0 && Laestabs.Contains(digits);
    }
}
