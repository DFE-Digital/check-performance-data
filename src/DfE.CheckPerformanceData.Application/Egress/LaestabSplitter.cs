namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// LDS wants the 7-digit DfE establishment number as two fields: a 3-digit LA number and a
/// 4-digit establishment number (AB#292610). Non-digits (the "/" some sources carry) are
/// ignored; anything that is not exactly seven digits is refused rather than padded.
/// </summary>
public static class LaestabSplitter
{
    public static bool TrySplit(string? raw, out string localAuthority, out string establishment)
    {
        var digits = new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length != 7)
        {
            localAuthority = string.Empty;
            establishment = string.Empty;
            return false;
        }

        localAuthority = digits[..3];
        establishment = digits[3..];
        return true;
    }
}
