namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>Which current version of a page holds embedded images. Older versions get no new version.</summary>
public enum EmbeddedVersionState
{
    Live,
    Scheduled,
    Draft
}
