using System.Globalization;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData.Columns;

/// <summary>
/// The per-window-type, per-population column definitions for the pupils table and its CSV
/// export.
///
/// The KS4 sets reproduce exactly what the hardcoded <c>_PupilTable.cshtml</c> headers and
/// <c>PupilCsvGenerator</c> produced before columns became data — guarded by
/// <c>PupilColumnSetsTests</c>.
///
/// The Post16 sets are a PLACEHOLDER agreed while the 16-19 column list is still with LDS:
/// the six shared demographics plus ULN (and inclusion status for the included population,
/// which is the only one with a P_INCL column). When the real list is confirmed, edit the
/// Post16 methods below — no other file needs to change.
/// </summary>
public static class PupilColumnSets
{
    public static IReadOnlyList<PupilColumn> Table(CheckingWindowType windowType, bool included) =>
        windowType == CheckingWindowType.Post16 ? Post16Table(included) : Ks4Table();

    public static IReadOnlyList<PupilColumn> Csv(CheckingWindowType windowType, bool included) =>
        windowType == CheckingWindowType.Post16 ? Post16Csv(included) : Ks4Csv();

    private static IReadOnlyList<PupilColumn> SharedDemographics() =>
    [
        new("Last name", p => p.Surname),
        new("First name", p => p.Firstname),
        new("Sex", p => p.Sex),
        new("Date of birth", p => PupilDateFormatter.ToDisplayDate(p.DateOfBirth)),
        new("Age", p => p.Age.ToString(CultureInfo.InvariantCulture)),
        new("CYPMD ID", p => p.Cypmd_Id)
    ];

    private static IReadOnlyList<PupilColumn> Ks4Table() => SharedDemographics();

    private static IReadOnlyList<PupilColumn> Post16Table(bool included)
    {
        List<PupilColumn> columns = [.. SharedDemographics(), new PupilColumn("ULN", p => p.Identifier)];

        // The non-included supplier file has no P_INCL column at all, so there is no status to show.
        if (included)
        {
            columns.Add(new PupilColumn("Inclusion status", p => p.Pincl?.ToString(CultureInfo.InvariantCulture) ?? string.Empty));
        }

        return columns;
    }

    private static IReadOnlyList<PupilColumn> Ks4Csv() =>
    [
        new("UPN", p => p.Identifier),
        new("CYPMD ID", p => p.Cypmd_Id),
        new("Surname", p => p.Surname),
        new("Forename", p => p.Firstname),
        new("Sex", p => p.Sex),
        new("Date of birth", p => PupilDateFormatter.ToDisplayDate(p.DateOfBirth)),
        new("Age", p => p.Age.ToString(CultureInfo.InvariantCulture)),
        new("Pupil Inclusion Status Flag", p => (p.Pincl ?? 0).ToString(CultureInfo.InvariantCulture)),
        new("Pupil Inclusion description", p => Ks4PinclDescriptions.GetValueOrDefault(p.Pincl ?? 0, string.Empty)),
        new("DfE Establishment Number", p => p.Laestab),
        new("School URN", p => Ks4(p).Urn.ToString(CultureInfo.InvariantCulture)),
        new("Admission date", p => PupilDateFormatter.ToDisplayDate(Ks4(p).EntryDate)),
        new("SEN", p => Ks4(p).SenF),
        new("Pupil's first language", p => Ks4(p).FirstLanguage),
        new("Pupil's ethnicity", p => Ks4(p).Ethnicity),
        new("Actual Year Group", p => Ks4(p).ActualYearGroup),
        new("Mobile", p => Ks4(p).NewMobile ? "1" : "0")
    ];

    private static IReadOnlyList<PupilColumn> Post16Csv(bool included)
    {
        List<PupilColumn> columns =
        [
            new PupilColumn("ULN", p => p.Identifier),
            new PupilColumn("CYPMD ID", p => p.Cypmd_Id),
            new PupilColumn("Surname", p => p.Surname),
            new PupilColumn("Forename", p => p.Firstname),
            new PupilColumn("Sex", p => p.Sex),
            new PupilColumn("Date of birth", p => PupilDateFormatter.ToDisplayDate(p.DateOfBirth)),
            new PupilColumn("Age", p => p.Age.ToString(CultureInfo.InvariantCulture)),
            new PupilColumn("DfE Establishment Number", p => p.Laestab),
            new PupilColumn("School URN", p => Post16(p).Urn),
            new PupilColumn("UKPRN", p => Post16(p).Ukprn)
        ];

        if (included)
        {
            columns.Add(new PupilColumn("Pupil Inclusion Status Flag", p => p.Pincl?.ToString(CultureInfo.InvariantCulture) ?? string.Empty));
            columns.Add(new PupilColumn("Learning aims inclusion flag", p => Post16(p).PinclAims?.ToString(CultureInfo.InvariantCulture) ?? string.Empty));
        }
        else
        {
            columns.Add(new PupilColumn("CampID 0", p => Post16(p).CampId0));
            columns.Add(new PupilColumn("CampID 1", p => Post16(p).CampId1));
        }

        return columns;
    }

    // A column set is only ever paired with the record type its window produces, so a mismatch
    // is a wiring bug and should fail loudly rather than render blanks.
    private static PupilRecord Ks4(IPupilRecord p) => (PupilRecord)p;

    private static Post16PupilRecord Post16(IPupilRecord p) => (Post16PupilRecord)p;

    private static readonly Dictionary<int, string> Ks4PinclDescriptions = new()
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
}
