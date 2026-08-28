using System.Globalization;
using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.Journey.Validators;

/// <summary>
/// Validates that a value is a plain whole number from 1 to 9999 — the cohort-count question on the
/// incorrect-grade enquiry journey (AB#296648).
///
/// Digits only: a sign, a decimal point or a thousands separator in a headcount is a typo rather than
/// a quantity, and accepting them would put a value the downstream document cannot represent as an
/// <c>int</c> into a submitted enquiry. The upper bound is a sanity cap — no single qualification
/// cohort at one school reaches four figures, so a larger number is a slipped keystroke.
///
/// Surrounding whitespace is trimmed (unlike <see cref="DfeNumberFormatValidator"/>, where the
/// pattern is an exact identifier): here the whitespace is a typing artefact and the answer it
/// denotes is unambiguous.
/// </summary>
public sealed partial class WholeNumberFormatValidator : IFormatValidator
{
    private const int Minimum = 1;
    private const int Maximum = 9999;

    public string Name => "WholeNumber";

    /// <summary>
    /// Must read identically to the question's <c>validationFailure</c> in the flow config: the
    /// engine reports the required-rule message for an empty answer and THIS message for a malformed
    /// one, so different copy would make the same field appear to have two different rules.
    /// </summary>
    public string FailureMessage => "Enter how many students have an incorrect grade for this qualification";

    public bool IsValid(string value)
    {
        var trimmed = value?.Trim() ?? string.Empty;

        if (!DigitsOnlyPattern().IsMatch(trimmed))
            return false;

        // A leading-zero form such as "0010" denotes 10, so parse rather than measure the string.
        // TryParse also rejects a digit run too long for an int instead of overflowing.
        return int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
               && parsed is >= Minimum and <= Maximum;
    }

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex DigitsOnlyPattern();
}
