namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>How a <see cref="SchoolFilter"/> treats the schools it names.</summary>
public enum SchoolFilterMode
{
    /// <summary>Every school except the named ones; a request with no LAESTAB is kept.</summary>
    Exclude,

    /// <summary>Only the named schools; a request with no LAESTAB is never taken.</summary>
    Only
}

/// <summary>
/// Which schools a sweep covers (AB#307773). <see cref="Exclude"/> with the schools in an open or
/// unsent extension is the exercise's own sweep; <see cref="Only"/> with one extension's schools is
/// that extension's. The comparison strips the slash from the request's LAESTAB, because the claim
/// is stored as <c>933/4290</c> and extensions store digits.
/// </summary>
/// <param name="Laestabs">Digit-only LAESTABs (<c>9334290</c>), distinct.</param>
/// <param name="Mode">Whether the named schools are left out or are the only ones taken.</param>
public sealed record SchoolFilter(IReadOnlyCollection<string> Laestabs, SchoolFilterMode Mode)
{
    /// <summary>Every school: the sweep as it was before extensions.</summary>
    public static readonly SchoolFilter None = new([], SchoolFilterMode.Exclude);

    /// <summary>Excluding nobody adds no condition to the query.</summary>
    public bool IsNone => Laestabs.Count == 0 && Mode == SchoolFilterMode.Exclude;

    /// <summary>
    /// Every school except <paramref name="laestabs"/>. Excluding nobody is <see cref="None"/> itself,
    /// so an exercise with no held extensions sweeps exactly as it did before them.
    /// </summary>
    public static SchoolFilter Exclude(IEnumerable<string> laestabs)
    {
        var distinct = laestabs.Distinct().ToArray();
        return distinct.Length == 0 ? None : new(distinct, SchoolFilterMode.Exclude);
    }

    /// <summary>Only <paramref name="laestabs"/>; an empty set takes nothing.</summary>
    public static SchoolFilter Only(IEnumerable<string> laestabs) =>
        new(laestabs.Distinct().ToArray(), SchoolFilterMode.Only);
}
