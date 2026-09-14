using DfE.CheckPerformanceData.Application.ResultsEnquiry;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData.Results;

/// <summary>
/// One row of the Results tab: a main-file result joined to the pupil it names, on CYPMD ID.
/// <see cref="Pupil"/> is null when the school's pupil file holds nobody with that id — the row is
/// kept with blank demographic cells, because a result that names nobody is something a school
/// should see rather than have silently dropped.
/// </summary>
public sealed record ResultRow(IPupilRecord? Pupil, StudentResultRecord Result);
