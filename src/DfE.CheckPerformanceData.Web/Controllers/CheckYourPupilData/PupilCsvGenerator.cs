using System.Text;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;

public static class PupilCsvGenerator
{
    private static readonly Dictionary<int, string> PinclDescriptions = new()
    {
        { 401, "Pupil on roll and included in key stage 4 (both NOR and results)." },
        { 403, "Pupil added back. Included in key stage 4 (both NOR and results)." },
        { 414, "Year group adjusted to 11. Pupil reported as Year 10 or below last year. Included in key stage 4 NOR and results." },
        { 421, "Pupil assumed to be on your roll. Included in key stage 4 data." },
        { 431, "Year group adjusted to 11. Pupil reported as Year 10 or below last year. Included in key stage 4 NOR and results." },
        { 402, "Pupil not on roll and omitted from all figures to be published." },
        { 404, "Pupil on roll. Pupil is not at the end of key stage 4 and is omitted from key stage 4 data." },
        { 407, "Pupil admitted following permanent exclusion from a maintained school. Omitted from NOR and results data." },
        { 408, "Pupil on roll. Pupil aged 15 admitted following permanent exclusion from a maintained school. Omitted from key stage 4." },
        { 410, "Pupil on roll but dual-registered with another school and is published elsewhere." },
        { 413, "Year group adjusted to 12. Pupil previously reported as end of key stage 4. Omitted from all data." },
        { 422, "Pupil assumed to be on your roll. Pupil is estimated not to be in year 11. Omitted from key stage 4." },
        { 430, "Year group adjusted to 12. Pupil previously reported as end of key stage 4. Omitted from all data." },
    };

    private static readonly string[] Headers =
    [
        "UPN", "CYPMD ID", "Surname", "Forename", "Sex", "Date of birth", "Age",
        "Pupil Inclusion Status Flag", "Pupil Inclusion description", "DfE Establishment Number",
        "School URN", "Admission date", "SEN", "Pupil's first language", "Pupil's ethnicity",
        "Actual Year Group", "Mobile"
    ];

    public static byte[] Generate(IReadOnlyList<PupilCsvDto> pupils)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Headers.Select(Escape)));

        foreach (var p in pupils)
        {
            PinclDescriptions.TryGetValue(p.Pincl, out var pinclDesc);
            sb.AppendLine(string.Join(",",
            [
                Escape(p.Upn),
                Escape(p.CypmdId),
                Escape(p.Surname),
                Escape(p.Firstname),
                Escape(p.Sex),
                Escape(p.DateOfBirth),
                Escape(p.Age.ToString()),
                Escape(p.Pincl.ToString()),
                Escape(pinclDesc ?? string.Empty),
                Escape(p.Laestab),
                Escape(p.Urn),
                Escape(p.EntryDate.ToString("dd/MM/yyyy")),
                Escape(p.SenF),
                Escape(p.FirstLanguage),
                Escape(p.Ethnicity),
                Escape(p.ActualYearGroup),
                Escape(p.NewMobile ? "1" : "0")
            ]));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
