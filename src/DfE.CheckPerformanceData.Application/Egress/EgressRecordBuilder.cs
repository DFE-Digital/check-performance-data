using System.Globalization;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>Mutable per-record state carried through steps 2-7. One instance per pulled record that survived the filter.</summary>
public sealed class EgressWorkItem(EgressSourceRecord source)
{
    public EgressSourceRecord Source { get; } = source;
    public string? CorrectionType { get; set; }
    public string? CorrectionReason { get; set; }
    public string LocalAuthority { get; set; } = string.Empty;
    public string Establishment { get; set; } = string.Empty;
    public string DateOfBirthIso { get; set; } = string.Empty;
    public string AdmissionDateIso { get; set; } = string.Empty;
    public List<EgressRecordFailure> Failures { get; } = [];
    public NewLearnerRow? NewRow { get; set; }
    public RemoveLearnerRow? RemoveRow { get; set; }

    public bool HasFailed => Failures.Count > 0;

    public void Fail(string step, string field, string reason) =>
        Failures.Add(new EgressRecordFailure(step, Source.TicketId, Source.ReferenceNumber, field, reason));
}

/// <summary>
/// Steps 2-6 of the preprocessing pipeline, one pure-ish function each, so the pipeline can report
/// them one at a time. A step that finds a problem records it against the field LDS would see and
/// leaves the item failed; later steps skip a failed item rather than piling on secondary errors.
/// The Zendesk ticket is the truth. Each LDS column that has a Zendesk field is read from the
/// ticket, and a missing value fails the record. Egress never works the value out instead. Only
/// the columns that have no Zendesk field come from CYPMD's data: ULN, a new learner's
/// Year_Group, Removal_Year_0..2 and Post_Code (always blank).
/// </summary>
public static class EgressRecordBuilder
{
    public const string StepCodes = "Read correction codes";
    public const string StepSplit = "Split DfE establishment number";
    public const string StepDates = "Standardise dates";
    public const string StepBuild = "Build LDS records";
    public const string StepTrim = "Trim values";

    public static void ReadCodes(EgressWorkItem item)
    {
        var type = TicketValue(item, StepCodes, "Correction_Type", ZendeskTicketFieldConstants.CorrectionTypeName);
        item.CorrectionType = type is null ? null : LdsCode(type);
        if (item.Source.OutputType != EgressOutputType.RemoveLearners) return;

        var reason = TicketValue(item, StepCodes, "Correction_Reason", ZendeskTicketFieldConstants.CorrectionReason31Name);
        item.CorrectionReason = reason is null ? null : LdsCode(reason);
    }

    // S4: steps 2-4 are independent of one another (a bad correction reason says nothing about
    // whether the LAESTAB is valid), so none of them gates on HasFailed — only Build/Trim/Validate
    // do, once every independent check has had its turn. A record with two unrelated faults lists
    // both instead of ops discovering the second one only on the next run.
    public static void SplitEstablishment(EgressWorkItem item)
    {
        var raw = TicketValue(item, StepSplit, "Establishment_Number", ZendeskTicketFieldConstants.DfeEstablishmentNumberName);
        if (raw is null) return;
        if (LaestabSplitter.TrySplit(raw, out var la, out var estab))
        {
            item.LocalAuthority = la;
            item.Establishment = estab;
            return;
        }
        item.Fail(StepSplit, "Establishment_Number", $"DfE establishment number '{raw}' is not 7 digits");
    }

    public static void StandardiseDates(EgressWorkItem item)
    {
        var dobRaw = TicketValue(item, StepDates, "Date_of_Birth", ZendeskTicketFieldConstants.DateOfBirthCypmdName);
        if (dobRaw is not null)
        {
            if (EgressDates.TryToIso(dobRaw, out var dob)) item.DateOfBirthIso = dob;
            else item.Fail(StepDates, "Date_of_Birth", $"Date of birth '{dobRaw}' is not a recognisable date");
        }

        if (item.Source.OutputType == EgressOutputType.NewLearners)
        {
            var admissionRaw = TicketValue(item, StepDates, "Admission_Date", ZendeskTicketFieldConstants.AdmissionDateName);
            if (admissionRaw is null) return;
            if (EgressDates.TryToIso(admissionRaw, out var admission)) item.AdmissionDateIso = admission;
            else item.Fail(StepDates, "Admission_Date", $"Admission date '{admissionRaw}' is not a recognisable date");
        }
    }

    public static void Build(EgressWorkItem item)
    {
        if (item.HasFailed) return;
        var s = item.Source;
        if (!s.JourneyFound)
        {
            item.Fail(StepBuild, "Correction_ID", "The request's journey record could not be read, so its values are unknown");
            return;
        }

        var keyStageOption = TicketValue(item, StepBuild, "Key_Stage", ZendeskTicketFieldConstants.KeyStageName);
        var stage = keyStageOption is null ? null : EgressOutputTypes.KeyStageValue(keyStageOption);
        if (keyStageOption is not null && stage is null)
            item.Fail(StepBuild, "Key_Stage", $"Key stage '{keyStageOption}' has no LDS Key_Stage value");
        var cycleYear = TicketValue(item, StepBuild, "Cycle_Year", ZendeskTicketFieldConstants.CycleYearName);
        var cycleMonth = TicketValue(item, StepBuild, "Cycle_Month", ZendeskTicketFieldConstants.CycleMonthName);
        var surname = TicketValue(item, StepBuild, "Surname", ZendeskTicketFieldConstants.SurnameCypmdName);
        var forename = TicketValue(item, StepBuild, "Forename", ZendeskTicketFieldConstants.ForenameCypmdName);
        var sex = Sex(item);
        var schoolUrn = s.OutputType == EgressOutputType.NewLearners
            ? TicketValue(item, StepBuild, "URN", ZendeskTicketFieldConstants.SchoolUrnName)
            : null;
        if (item.HasFailed) return;

        var ticket = s.TicketId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        var learnerId = s.TicketField(ZendeskTicketFieldConstants.LdsMatchedPupilIdName) ?? string.Empty;

        if (s.OutputType == EgressOutputType.RemoveLearners)
        {
            var (year0, year1, year2) = RemovalYears(s, cycleYear!);
            item.RemoveRow = new RemoveLearnerRow(
                CorrectionId: ticket, CorrectionType: item.CorrectionType ?? string.Empty, CorrectionReason: item.CorrectionReason ?? string.Empty,
                KeyStage: stage!, EstablishmentNumber: item.Establishment, Surname: surname!, Forename: forename!,
                Sex: sex!, DateOfBirth: item.DateOfBirthIso, CycleYear: cycleYear!, CycleMonth: cycleMonth!,
                LocalAuthority: item.LocalAuthority, LearnerId: learnerId,
                ChangeRequestId: s.ChangeRequestId, TicketId: s.TicketId, ReferenceNumber: s.ReferenceNumber)
            {
                YearGroup = YearGroupMovedTo(s), RemovalYear0 = year0, RemovalYear1 = year1, RemovalYear2 = year2
            };
            return;
        }

        // ULN and Year_Group have no Zendesk field, so they come from the journey.
        item.NewRow = new NewLearnerRow(
            CorrectionId: ticket, CorrectionType: item.CorrectionType ?? string.Empty, KeyStage: stage!,
            LocalAuthority: item.LocalAuthority, EstablishmentNumber: item.Establishment,
            Surname: surname!, Forename: forename!, Sex: sex!,
            DateOfBirth: item.DateOfBirthIso, AdmissionDate: item.AdmissionDateIso, Postcode: string.Empty,
            CycleYear: cycleYear!, CycleMonth: cycleMonth!, SchoolUrn: schoolUrn!,
            Uln: s.WindowType == CheckingWindowType.Post16 ? s.PupilIdentifier ?? string.Empty : string.Empty,
            Upn: (s.TicketField(ZendeskTicketFieldConstants.UpnName) ?? string.Empty).ToUpperInvariant(),
            LearnerId: learnerId,
            YearGroup: s.Answer("year-group") ?? string.Empty,
            ChangeRequestId: s.ChangeRequestId, TicketId: s.TicketId, ReferenceNumber: s.ReferenceNumber);
    }

    public static void Trim(EgressWorkItem item)
    {
        if (item.HasFailed) return;
        if (item.RemoveRow is { } r)
            item.RemoveRow = r with
            {
                CorrectionId = r.CorrectionId.Trim(), CorrectionType = r.CorrectionType.Trim(), CorrectionReason = r.CorrectionReason.Trim(),
                KeyStage = r.KeyStage.Trim(), EstablishmentNumber = r.EstablishmentNumber.Trim(), Surname = r.Surname.Trim(), Forename = r.Forename.Trim(),
                Sex = r.Sex.Trim(), DateOfBirth = r.DateOfBirth.Trim(), CycleYear = r.CycleYear.Trim(), CycleMonth = r.CycleMonth.Trim(),
                LocalAuthority = r.LocalAuthority.Trim(), LearnerId = r.LearnerId.Trim(), YearGroup = r.YearGroup.Trim(),
                RemovalYear0 = r.RemovalYear0.Trim(), RemovalYear1 = r.RemovalYear1.Trim(), RemovalYear2 = r.RemovalYear2.Trim()
            };
        if (item.NewRow is { } n)
            item.NewRow = n with
            {
                CorrectionId = n.CorrectionId.Trim(), CorrectionType = n.CorrectionType.Trim(), KeyStage = n.KeyStage.Trim(),
                LocalAuthority = n.LocalAuthority.Trim(), EstablishmentNumber = n.EstablishmentNumber.Trim(), Surname = n.Surname.Trim(),
                Forename = n.Forename.Trim(), Sex = n.Sex.Trim(), DateOfBirth = n.DateOfBirth.Trim(),
                AdmissionDate = n.AdmissionDate.Trim(), Postcode = n.Postcode.Trim(), CycleYear = n.CycleYear.Trim(), CycleMonth = n.CycleMonth.Trim(),
                SchoolUrn = n.SchoolUrn.Trim(), Uln = n.Uln.Trim(), Upn = n.Upn.Trim(), LearnerId = n.LearnerId.Trim(),
                YearGroup = n.YearGroup.Trim()
            };
    }

    // v2.4 Remove Learner row 22: KS4 files carry Year_Group "for year group change requests only".
    // The ticket's "Pupil year group (CYPMD)" holds it for a year group change only.
    private static string YearGroupMovedTo(EgressSourceRecord s) =>
        s.WindowType is CheckingWindowType.KS4June or CheckingWindowType.KS4Autumn
            ? s.TicketField(ZendeskTicketFieldConstants.PupilYearGroupCypmdName) ?? string.Empty
            : string.Empty;

    // v2.4 Remove Learner rows 23-25: 16-19 files carry TRUE/FALSE per academic year. The Post16
    // Remove journey's years-to-remove checkbox uses labels "YYYY-YYYY" (EgressAnswers joins them
    // with "|"); Year_0 is the academic year ending in Cycle_Year, Year_1 the one before, Year_2
    // the one before that. Only the "other" route asks the question — every other reason leaves
    // all three blank (NULL-able in the spec) rather than guessing.
    private static (string Year0, string Year1, string Year2) RemovalYears(EgressSourceRecord s, string cycleYear)
    {
        var answer = s.Answer("years-to-remove");
        if (s.WindowType != CheckingWindowType.Post16 || string.IsNullOrWhiteSpace(answer) || !int.TryParse(cycleYear, out var end))
            return (string.Empty, string.Empty, string.Empty);
        var chosen = answer.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        string Flag(int n) => chosen.Contains($"{end - 1 - n}-{end - n}") ? "TRUE" : "FALSE";
        return (Flag(0), Flag(1), Flag(2));
    }

    // The Zendesk "Sex" field has only the options m and f. A pupil whose sex is not known has no
    // value on the ticket, so CYPMD's "U" is the one value egress takes from its own data.
    private static string? Sex(EgressWorkItem item)
    {
        if (item.Source.TicketField(ZendeskTicketFieldConstants.SexName) is { } sex) return sex.Trim().ToUpperInvariant();
        var cypmd = item.Source.OutputType == EgressOutputType.NewLearners
            ? item.Source.Answer("sex") ?? item.Source.PupilSex
            : item.Source.PupilSex;
        if (string.Equals(cypmd?.Trim(), "U", StringComparison.OrdinalIgnoreCase)) return "U";
        item.Fail(StepBuild, "Sex", MissingTicketValue(ZendeskTicketFieldConstants.SexName));
        return null;
    }

    // Reads one value from the ticket. A missing value fails the record against the LDS column.
    private static string? TicketValue(EgressWorkItem item, string step, string column, string fieldName)
    {
        var value = item.Source.TicketField(fieldName)?.Trim();
        if (value is null) item.Fail(step, column, MissingTicketValue(fieldName));
        return value;
    }

    private static string MissingTicketValue(string fieldName) => $"The Zendesk ticket has no \"{fieldName}\" value";

    // A Zendesk correction option is the LDS code with a suffix: "31_" is type 31, "8_31" is reason 8.
    private static string LdsCode(string option)
    {
        var underscore = option.IndexOf('_');
        return underscore > 0 ? option[..underscore] : option;
    }
}
