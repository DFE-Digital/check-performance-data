using System.Globalization;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData.Results;

/// <summary>
/// The column definitions for the Results tab and its CSV export. The table carries the seven
/// agreed columns; the CSV adds the result detail the table has no room for. Subject is the
/// supplier's QUAL_NAME — the closest thing the results file has to a subject, and the text the
/// enquiry journey already shows for a result.
/// </summary>
public static class ResultColumnSets
{
    public static IReadOnlyList<ResultColumn> Table() =>
    [
        new("Last name", r => r.Pupil?.Surname ?? string.Empty),
        new("First name", r => r.Pupil?.Firstname ?? string.Empty),
        new("Sex", r => r.Pupil?.Sex ?? string.Empty),
        new("Date of birth", r => r.Pupil is null ? string.Empty : PupilDateFormatter.ToDisplayDate(r.Pupil.DateOfBirth)),
        new("Age", r => r.Pupil?.Age.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
        // From the result, not the pupil, so an unmatched row still says who it is about.
        new("CYPMD ID", r => r.Result.CypmdId),
        new("Subject", r => r.Result.QualificationName)
    ];

    public static IReadOnlyList<ResultColumn> Csv() =>
    [
        .. Table(),
        new("QAN", r => r.Result.Qan),
        new("Session", r => r.Result.Session),
        new("Grade", r => r.Result.Grade),
        new("Source file", r => r.Result.SourceFile)
    ];
}
