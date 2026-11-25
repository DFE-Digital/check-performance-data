using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.WindowManagement.SampleData;

/// <summary>
/// #937: the LAESTAB and URN that an admin types for a sample school. The LAESTAB can have its
/// slash (933/4290) or not (9334290). It is stored as digits only.
/// </summary>
public static partial class SampleSchoolInput
{
    public const string LaestabMissing = "Enter a LAESTAB";
    public const string LaestabInvalid = "Enter a LAESTAB as 7 digits, like 933/4290 or 9334290";
    public const string UrnMissing = "Enter a URN";
    public const string UrnInvalid = "Enter a URN as 6 digits, like 123456";

    public static SampleSchoolInputResult Check(string? laestab, string? urn)
    {
        string? laestabError = null;
        string? urnError = null;
        string? laestabDigits = null;
        string? urnDigits = null;

        var laestabText = laestab?.Trim() ?? string.Empty;
        if (laestabText.Length == 0)
            laestabError = LaestabMissing;
        else if (LaestabPattern().Match(laestabText) is { Success: true } match)
            laestabDigits = match.Groups[1].Value + match.Groups[2].Value;
        else
            laestabError = LaestabInvalid;

        var urnText = urn?.Trim() ?? string.Empty;
        if (urnText.Length == 0)
            urnError = UrnMissing;
        else if (UrnPattern().IsMatch(urnText))
            urnDigits = urnText;
        else
            urnError = UrnInvalid;

        return new SampleSchoolInputResult(laestabDigits, urnDigits, laestabError, urnError);
    }

    // ECMAScript keeps \d to 0-9. .NET's \d alone also matches the digits of other scripts.
    [GeneratedRegex(@"^(\d{3})/?(\d{4})$", RegexOptions.ECMAScript)]
    private static partial Regex LaestabPattern();

    [GeneratedRegex(@"^\d{6}$", RegexOptions.ECMAScript)]
    private static partial Regex UrnPattern();
}

/// <summary>The checked values (null when not valid) and an error for each field that is not valid.</summary>
public sealed record SampleSchoolInputResult(string? Laestab, string? Urn, string? LaestabError, string? UrnError)
{
    public bool IsValid => LaestabError is null && UrnError is null;
}
