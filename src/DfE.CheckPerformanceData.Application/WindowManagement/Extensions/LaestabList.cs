using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.WindowManagement.Extensions;

/// <summary>
/// AB#307773: the schools an extension names, as an admin pastes them — one LAESTAB per line, with
/// or without its slash. Stored and compared as digits only, the form
/// <see cref="Dashboard.LaestabNormaliser"/> and every blob name use; shown with the slash.
/// </summary>
public static partial class LaestabList
{
    public static LaestabListResult Parse(string? text)
    {
        if (string.IsNullOrEmpty(text)) return new LaestabListResult([], []);

        var laestabs = new List<string>();
        var errors = new List<LaestabLineError>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            // Trim takes the \r of a Windows line ending with the spaces.
            var value = lines[i].Trim();
            if (value.Length == 0) continue;

            var match = LaestabPattern().Match(value);
            if (!match.Success)
            {
                // Numbered over every line, blanks included: it is the line the admin sees in the box.
                errors.Add(new LaestabLineError(i + 1, value));
                continue;
            }

            var digits = match.Groups[1].Value + match.Groups[2].Value;
            // A school pasted twice, in either form, is named once.
            if (!laestabs.Contains(digits)) laestabs.Add(digits);
        }

        return new LaestabListResult(laestabs, errors);
    }

    /// <summary>"9334290" → "933/4290". Anything not 7 digits is shown as it is.</summary>
    public static string Display(string digits) =>
        digits.Length == 7 ? $"{digits[..3]}/{digits[3..]}" : digits;

    /// <summary>One LAESTAB per line, for a textarea read back to the admin.</summary>
    public static string DisplayLines(IEnumerable<string> digits) => string.Join("\n", digits.Select(Display));

    // ECMAScript keeps \d to 0-9: .NET's \d alone also matches other scripts' digits, which no
    // LAESTAB holds.
    [GeneratedRegex(@"^(\d{3})/?(\d{4})$", RegexOptions.ECMAScript)]
    private static partial Regex LaestabPattern();
}

/// <summary>A pasted line that is not a LAESTAB. <see cref="Line"/> is 1-based over every line, blanks included.</summary>
public sealed record LaestabLineError(int Line, string Value)
{
    public string Message => $"Line {Line}: '{Value}' is not a LAESTAB. Enter 7 digits, as 933/4290 or 9334290.";
}

/// <summary>The distinct LAESTABs (digits, input order) and the lines that were not LAESTABs.</summary>
public sealed record LaestabListResult(IReadOnlyList<string> Laestabs, IReadOnlyList<LaestabLineError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
