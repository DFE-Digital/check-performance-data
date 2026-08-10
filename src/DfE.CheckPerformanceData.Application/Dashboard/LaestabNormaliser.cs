namespace DfE.CheckPerformanceData.Application.Dashboard;

/// <summary>
/// Reduces a laestab to digits only ("933/4070" → "9334070") so values from DfE Sign-In
/// claims join against the digits-only laestabs embedded in pupil-blob names.
/// </summary>
public static class LaestabNormaliser
{
    public static string Normalise(string? laestab)
        => laestab is null ? string.Empty : new string(laestab.Where(char.IsAsciiDigit).ToArray());
}
