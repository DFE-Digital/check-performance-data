using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.Common;

/// <summary>
/// The rule for text in a CSV file that a person opens in a spreadsheet. A spreadsheet reads a
/// cell that starts with a trigger character as a formula (OWASP CSV injection). The trigger
/// characters are <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, tab, carriage return, line feed and
/// the full-width forms of <c>=</c>, <c>+</c>, <c>-</c> and <c>@</c>.
/// </summary>
public static partial class SpreadsheetFormula
{
    private const string TriggerCharacters = "=+-@\t\r\n＝＋－＠";
    private const char TextMarker = '\'';
    private const string Number = @"(?:[0-9]+\.?[0-9]*|\.[0-9]+)(?:[eE][+-]?[0-9]+)?";
    private const string Interval = @" \([+-]?" + Number + " to [+-]?" + Number + @"\)";

    /// <summary>Says if the text starts with a trigger character.</summary>
    /// <param name="text">The text of one cell. It must not be null.</param>
    /// <returns>True when the first character is a trigger character. False for empty text.</returns>
    public static bool StartsWithTrigger(string text) =>
        text.Length > 0 && TriggerCharacters.Contains(text[0]);

    /// <summary>Gives the text in a form that a spreadsheet shows and does not calculate.</summary>
    /// <param name="text">The text of one cell. It must not be null.</param>
    /// <returns>
    /// The text with an apostrophe before it when it starts with a trigger character. Text with
    /// no trigger character at its start comes back as it is. So do the supplier's signed value
    /// forms, which cannot be a formula: a signed number (<c>-1.00</c>, <c>-1.5E-07</c>), a
    /// signed number with a percent sign (<c>-5%</c>), a signed number with a bracketed interval
    /// (<c>-1.10 (-2.35 to 0.15)</c>), and a lone <c>+</c> or <c>-</c>.
    /// </returns>
    public static string Neutralise(string text) =>
        StartsWithTrigger(text) && !SupplierSignedValue().IsMatch(text) ? TextMarker + text : text;

    // A lone ASCII sign, or a signed number that can end with a percent sign or a bracketed interval.
    [GeneratedRegex(@"\A[+-](?:" + Number + "(?:%|" + Interval + @")?)?\z")]
    private static partial Regex SupplierSignedValue();
}
