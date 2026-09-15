using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>Every date LDS receives is yyyy-MM-dd. Wraps PupilDateFormatter so the accepted input formats stay in one place.</summary>
public static class EgressDates
{
    public static bool TryToIso(string? raw, out string iso)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            iso = string.Empty;
            return false;
        }

        return PupilDateFormatter.TryToIsoDate(raw.Trim(), out iso);
    }
}
