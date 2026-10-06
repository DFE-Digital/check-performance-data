namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// AB#304900: how many evidence files one upload question may hold. Written once here so the
/// rule that refuses a seventh file and the page that says "N of 6 files added" cannot disagree.
/// </summary>
/// <remarks>
/// This is a count of files. It is not the page limit (<c>maxEvidencePages</c> on
/// <see cref="JourneyValidationService"/>), which is a separate rule and is switched off.
/// </remarks>
public static class EvidenceUploadLimits
{
    public const int MaxFiles = 6;
}
