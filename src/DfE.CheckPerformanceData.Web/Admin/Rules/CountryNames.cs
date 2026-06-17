using System.Globalization;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>
/// Resolves an ISO 3166-1 alpha-2 country code to an English display name via the runtime's
/// ICU data (<see cref="RegionInfo"/>), so the lookups page can show a readable country alongside
/// the code without a database dependency. Falls back to the raw code for anything not recognised.
/// </summary>
public static class CountryNames
{
    public static string DisplayName(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return code;

        // RegionInfo also accepts culture names, and on Linux ICU resolves malformed input
        // like "GB-ENG" to a bogus region ("ENG") instead of throwing as Windows NLS does.
        // Only the two-letter alpha-2 shape is ever a country code here; anything else falls
        // back without consulting RegionInfo so behaviour is identical on every platform.
        if (code.Length != 2 || !char.IsAsciiLetter(code[0]) || !char.IsAsciiLetter(code[1]))
            return code;

        try { return new RegionInfo(code).EnglishName; }
        catch (ArgumentException) { return code; } // unknown/invalid code → show the code
    }
}
