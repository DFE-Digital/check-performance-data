namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// Which pupils a search returns. <see cref="Included"/> and <see cref="All"/> read the
/// <c>INCLUDED</c> stamp. <see cref="Includable"/> and <see cref="NotIncludable"/> read the
/// <c>INCLUDABLE</c> stamp (#711): the Include journey searches <see cref="Includable"/> — on
/// 16-19 that adds the included file's P_INCL 502 students to the non-included file — and its
/// "already included" guards look a name up among <see cref="NotIncludable"/>.
/// </summary>
public enum PupilFilter { Included, All, Includable, NotIncludable }
