namespace DfE.CheckPerformanceData.Application.ContentStaging;

/// <summary>
/// Coercions applied by the bundle DTOs' init accessors.
///
/// A property initialiser (<c>= string.Empty</c>, <c>= []</c>) only runs when the JSON omits
/// the member. When the JSON says <c>null</c> explicitly, System.Text.Json assigns null over
/// the top and the declared non-nullability becomes a lie — every reader downstream then
/// dereferences something the type promised was there. A bundle is untrusted input, and
/// <c>"content": null</c> is a one-token edit of a legitimate one, so the guarantee has to be
/// enforced where the value enters rather than re-checked at every use.
/// </summary>
internal static class BundleMemberDefaults
{
    internal static string OrEmpty(string? value) => value ?? string.Empty;

    /// <summary>
    /// Never null, and never containing nulls — a JSON array is free to hold null elements
    /// (<c>"versions": [null]</c>) and those would fail exactly like a null collection.
    /// </summary>
    internal static List<T> NonNullItems<T>(List<T>? value) where T : class =>
        value is null ? [] : value.FindAll(static item => item is not null);
}
