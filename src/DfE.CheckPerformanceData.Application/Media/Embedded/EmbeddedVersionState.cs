namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>Which current version of a page holds embedded images. Older versions are never changed.</summary>
public enum EmbeddedVersionState
{
    Live,
    Scheduled,
    Draft
}
