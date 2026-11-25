using System.Globalization;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.Application.WindowManagement.SampleData;
using Newtonsoft.Json.Schema;
using static DfE.CheckPerformanceData.Infrastructure.Ingress.SampleData.SampleValues;

namespace DfE.CheckPerformanceData.Infrastructure.Ingress.SampleData;

/// <summary>What one row of a file is about.</summary>
public enum SampleRowKind
{
    /// <summary>One row for each pupil or student.</summary>
    Pupils,

    /// <summary>Two rows (two qualifications) for each pupil or student.</summary>
    Results,

    /// <summary>A late results file: one row for every fifth pupil or student.</summary>
    LateResults,

    /// <summary>One row for the school.</summary>
    Summary
}

/// <summary>
/// #940, #941: makes rows from the schema of the file. It fills each property that it knows by
/// name (identifiers, names, dates, sex, the inclusion code, qualifications and grades). It fills
/// other numbers with small values and leaves other text blank. Every value is made up.
/// </summary>
/// <remarks>
/// <para>
/// A pupil has the same identifiers (UPN, ULN, CYPMD ID, names) in each file of a school, so
/// results belong to the pupils of the pupil file. <paramref name="firstPupil"/> moves a file to
/// other pupils, for example the non-included students of 16-19.
/// </para>
/// <para>
/// A value that breaks the property's pattern, enum or range becomes blank, and text is cut to
/// its maximum length. Each property of a supplier schema can be null, so a blank value is valid.
/// </para>
/// </remarks>
public sealed class SchemaSampleGenerator(
    string name, SampleRowKind kind, int birthYear, IReadOnlyList<SampleQualification> qualifications, int firstPupil = 0)
    : ISampleDataGenerator
{
    public string Name => name;

    /// <summary>The pupils of a school: 24 to 32, from its LAESTAB.</summary>
    public static int PupilCount(string laestab) => 24 + SchoolIndex(laestab) * 2;

    public IEnumerable<IReadOnlyDictionary<string, string>> Rows(SampleSchool school, SampleSchemaShape schema)
    {
        var count = PupilCount(school.Laestab);
        switch (kind)
        {
            case SampleRowKind.Summary:
                yield return Row(school, schema, firstPupil, null, count);
                break;
            case SampleRowKind.Pupils:
                for (var i = firstPupil; i < firstPupil + count; i++)
                    yield return Row(school, schema, i, null, count);
                break;
            case SampleRowKind.Results:
                for (var i = firstPupil; i < firstPupil + count; i++)
                {
                    yield return Row(school, schema, i, qualifications[i % qualifications.Count], count);
                    yield return Row(school, schema, i, qualifications[(i + 1) % qualifications.Count], count);
                }
                break;
            case SampleRowKind.LateResults:
                for (var i = firstPupil; i < firstPupil + count; i += 5)
                    yield return Row(school, schema, i, qualifications[(i + 2) % qualifications.Count], count);
                break;
        }
    }

    private Dictionary<string, string> Row(SampleSchool school, SampleSchemaShape schema, int i,
        SampleQualification? qualification, int count)
    {
        var row = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in schema.Fields)
        {
            // Ingress stamps these from the schema's rules.
            if (field.Type.HasFlag(JSchemaType.Boolean) || field.Type.HasFlag(JSchemaType.Array) || field.Type.HasFlag(JSchemaType.Object))
                continue;

            var value = field.Name == schema.InclusionField
                ? InclusionCode(schema, i)
                : Value(field, school, i, qualification, count);
            row[field.Name] = Fit(field, value);
        }
        return row;
    }

    private static string InclusionCode(SampleSchemaShape schema, int i)
    {
        // Most pupils have the first included code. Every sixth pupil has another code. A rule
        // that only describes codes gives its first code to most pupils.
        var main = schema.IncludedCodes.Count > 0 ? schema.IncludedCodes : schema.OtherCodes;
        var others = schema.IncludedCodes.Count > 0 ? schema.OtherCodes : schema.OtherCodes.Skip(1).ToList();
        if (others.Count > 0 && i % 6 == 5)
            return others[i / 6 % others.Count];
        return main.Count > 0 ? main[0] : string.Empty;
    }

    private string Value(SampleSchemaField field, SampleSchool school, int i, SampleQualification? qualification, int count)
    {
        // LAESTAB_0, SURNAME_0 and so on are the same values from another source.
        var key = field.Name.EndsWith("_0", StringComparison.Ordinal) ? field.Name[..^2] : field.Name;
        var name = (i * 7 + SchoolIndex(school.Laestab) * 13) % (Firstnames.Count * Surnames.Count);
        var dob = new DateOnly(birthYear, 9, 1).AddDays(i * 37 % 365);

        switch (key.ToUpperInvariant())
        {
            case "LAESTAB": return school.Laestab;
            case "URN": return school.Urn;
            case "UPN": return $"S{school.Laestab}26{i:D3}";
            case "ULN": return $"9{school.Laestab}{i % 100:D2}";
            case "CYPMD_ID" or "CYPMD_PK" or "MATCHREF": return $"8{school.Laestab[^4..]}{i:D4}";
            case "CANDNO": return (1000 + i).ToString("D7", CultureInfo.InvariantCulture);
            case "SURNAME": return Surnames[name / Firstnames.Count];
            case "FORENAME" or "FORENAMES": return Firstnames[name % Firstnames.Count];
            case "SEX" or "GENDER": return i % 2 == 0 ? "F" : "M";
            case "DOB": return Iso(dob);
            case "ENTRYDAT": return Iso(new DateOnly(birthYear + 11, 9, 1));
            case "AGE": return Number(2026 - birthYear - 1);
        }

        if (qualification is not null)
        {
            switch (key.ToUpperInvariant())
            {
                case "QAN" or "GNUMBER": return qualification.Qan;
                case "QUAL_NAME" or "QUALIFICATION_DESCRIPTION" or "QUALIFICATION_NAME" or "SYLLABUS_TITLE": return qualification.Title;
                case "SHORT_QUAL_DESC" or "QUALIFICATION_TYPE": return qualification.Type;
                case "SUBJECT_NAME" or "SUBJECTDESCRIPTION": return qualification.Subject;
                case "SYLLABUS" or "BRDSUBNO": return qualification.Syllabus;
                case "AB_CODE_NDAQ" or "AWARDING_BODY": return qualification.AwardingBody;
                case "SESSION" or "SEASON_AND_YEAR" or "EXAM_YEAR_SEASON": return "S2026";
                case "GRADE": return qualification.Grades[i % qualification.Grades.Count];
                case "LATE_RESULT_TYPE": return "New";
            }
        }

        // A whole number the file does not name is most often a 0 or 1 flag. In a summary it is a count.
        if (field.Type.HasFlag(JSchemaType.Integer))
            return Number(Math.Clamp(kind == SampleRowKind.Summary ? count : (i + key.Length) % 2,
                (int)(field.Minimum ?? 0), (int)(field.Maximum ?? int.MaxValue)));
        if (field.Type.HasFlag(JSchemaType.Number))
            return TwoPlaces(kind == SampleRowKind.Summary ? count : (i + key.Length) % 9);
        return string.Empty;
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Fit(SampleSchemaField field, string value)
    {
        if (value.Length == 0)
            return value;
        if (field.Enum is { } allowed && !allowed.Contains(value))
            return allowed.Count > 0 ? allowed[0] : string.Empty;
        if (field.Pattern is { } pattern && !Regex.IsMatch(value, pattern, RegexOptions.None, TimeSpan.FromSeconds(1)))
            return string.Empty;
        if (field.MaxLength is { } max && value.Length > max)
            return value[..(int)max];
        return value;
    }
}

/// <summary>A qualification of a sample result. The QANs are in the qualification reference.</summary>
public sealed record SampleQualification(
    string Qan, string Title, string Type, string Subject, string Syllabus, string AwardingBody, IReadOnlyList<string> Grades)
{
    public static readonly IReadOnlyList<SampleQualification> Gcses =
    [
        new("60146084", "GCSE (9-1) Mathematics", "GCSE (9-1) Full Course", "Mathematics", "8300H", "AQA", ["4", "5", "6", "7", "3"]),
        new("60142923", "GCSE (9-1) English Language", "GCSE (9-1) Full Course", "English Language", "8700", "AQA", ["5", "4", "6", "3", "7"])
    ];

    public static readonly IReadOnlyList<SampleQualification> ALevels =
    [
        new("60139948", "GCE A level Sociology", "GCE A level", "Sociology", "7192", "AQA", ["B", "C", "A", "D"]),
        new("60143368", "GCE A level Business", "GCE A level", "Business", "7132", "AQA", ["C", "B", "A", "E"]),
        new("6014371X", "GCE A level Economics", "GCE A level", "Economics", "7136", "AQA", ["A", "B", "C", "*"])
    ];
}
