using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.Journey.Validators;

/// <summary>
/// Validates that a value is a DfE number in either of its two accepted forms:
/// <c>nnn/nnnn</c> (e.g. <c>123/4567</c>) or the seven-digit <c>nnnnnnn</c>
/// (e.g. <c>1234567</c>). The two forms denote the same laestab; this rule only
/// validates format and does not normalise between them.
/// </summary>
public sealed partial class DfeNumberFormatValidator : IFormatValidator
{
    public string Name => "DfeNumber";

    public string FailureMessage => "Enter a DfE number in the format 123/4567 or 1234567";

    public bool IsValid(string value) => DfeNumberPattern().IsMatch(value);

    [GeneratedRegex(@"^\d{3}/\d{4}$|^\d{7}$")]
    private static partial Regex DfeNumberPattern();
}
