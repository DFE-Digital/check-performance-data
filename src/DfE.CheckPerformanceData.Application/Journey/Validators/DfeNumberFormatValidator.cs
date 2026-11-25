using DfE.CheckPerformanceData.Application.Dashboard;
using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.Journey.Validators;

/// <summary>
/// Validates that a value is a DfE number in either of its two accepted forms:
/// <c>nnn/nnnn</c> (e.g. <c>123/4567</c>) or the seven-digit <c>nnnnnnn</c>
/// (e.g. <c>1234567</c>). The two forms denote the same laestab; this rule only
/// validates format. <see cref="Normalise"/> gives the 7-digit form for the request document.
/// </summary>
public sealed partial class DfeNumberFormatValidator : IFormatValidator
{
    public string Name => "DfeNumber";

    public string FailureMessage => "Enter a DfE number in the format 123/4567 or 1234567";

    public bool IsValid(string value) => DfeNumberPattern().IsMatch(value);

    /// <summary>Sends the number as 7 digits, with no slash (#920).</summary>
    public string Normalise(string value) => LaestabNormaliser.Normalise(value);

    [GeneratedRegex(@"^\d{3}/\d{4}$|^\d{7}$")]
    private static partial Regex DfeNumberPattern();
}
