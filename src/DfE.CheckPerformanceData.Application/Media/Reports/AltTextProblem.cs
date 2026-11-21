namespace DfE.CheckPerformanceData.Application.Media.Reports;

/// <summary>Why alt text does not describe its image. <see cref="WeakAltText"/> decides it.</summary>
public enum AltTextProblem
{
    None,
    Missing,
    Generic,
    FileName,
    TooShort,
    SameAsCaption
}
