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

    /// <summary>Says if the text starts with a trigger character.</summary>
    /// <param name="text">The text of one cell. It must not be null.</param>
    /// <returns>True when the first character is a trigger character. False for empty text.</returns>
    public static bool StartsWithTrigger(string text) =>
        text.Length > 0 && TriggerCharacters.Contains(text[0]);

    /// <summary>Gives the text in a form that a spreadsheet shows and does not calculate.</summary>
    /// <param name="text">The text of one cell. It must not be null.</param>
    /// <returns>
    /// The text with an apostrophe before it when it starts with a trigger character. A signed
    /// number, for example <c>-1.00</c> or <c>-1.5E-07</c>, and text with no trigger character
    /// at its start come back as they are.
    /// </returns>
    public static string Neutralise(string text) =>
        StartsWithTrigger(text) && !SignedNumber().IsMatch(text) ? TextMarker + text : text;

    // An ASCII sign, ASCII digits with at most one decimal point, and an optional exponent.
    [GeneratedRegex(@"\A[+-](?:[0-9]+\.?[0-9]*|\.[0-9]+)(?:[eE][+-]?[0-9]+)?\z")]
    private static partial Regex SignedNumber();
}
