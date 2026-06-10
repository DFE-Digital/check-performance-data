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
        try { return new RegionInfo(code).EnglishName; }
        catch (ArgumentException) { return code; } // unknown/invalid code → show the code
    }
}
