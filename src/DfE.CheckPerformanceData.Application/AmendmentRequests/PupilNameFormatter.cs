namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

/// <summary>Formats a pupil's display name from first/surname, falling back to "N/A" when blank.</summary>
public static class PupilNameFormatter
{
    public static string Format(string? firstname, string? surname)
    {
        var name = $"{firstname} {surname}".Trim();
        return string.IsNullOrWhiteSpace(name) ? "N/A" : name;
    }
}
