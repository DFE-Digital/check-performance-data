namespace DfE.CheckPerformanceData.Application.Journey.Validators;

/// <summary>
/// The "permanent-exclusion-dfe-number" question's own format rule (issue 496). It must NOT reuse
/// the generic <see cref="DfeNumberFormatValidator"/> message (FR-004): every failure of this field
/// surfaces the pinned design copy "Enter the 7 digit DfE number of the school which permanently
/// excluded the pupil", so the required (blank) case — the config's <c>validationFailure</c> — and
/// the malformed case — THIS message — read identically to the user. The value rules are shared:
/// an opinionated identifier either in <c>nnn/nnnn</c> or <c>nnnnnnn</c> form, exact match, no
/// normalisation (see <see cref="DfeNumberFormatValidator"/>).
/// </summary>
public sealed partial class PermanentExclusionDfeNumberFormatValidator : IFormatValidator
{
    public string Name => "PermanentExclusionDfeNumber";

    /// <summary>
    /// Must read identically to the question's <c>validationFailure</c> in the flow config: the
    /// engine reports the required-rule message for an empty answer and THIS message for a malformed
    /// one, so different copy would make the same field appear to have two different rules.
    /// </summary>
    public string FailureMessage =>
        "Enter the 7 digit DfE number of the school which permanently excluded the pupil";

    public bool IsValid(string value) => DfeNumberPattern().IsMatch(value);

    // Duplicated from DfeNumberFormatValidator rather than delegating so the format rule stays
    // visible here in full; the same GeneratedRegex is shared by the framework at compile time.
    [System.Text.RegularExpressions.GeneratedRegex(@"^\d{3}/\d{4}$|^\d{7}$")]
    private static partial System.Text.RegularExpressions.Regex DfeNumberPattern();
}