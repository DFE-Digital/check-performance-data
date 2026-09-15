using System.Globalization;
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
/// </summary>
public static class EgressRecordBuilder
{
    public const string StepCodes = "Derive correction codes";
    public const string StepSplit = "Split DfE establishment number";
    public const string StepDates = "Standardise dates";
    public const string StepBuild = "Build LDS records";
    public const string StepTrim = "Trim values";

    public static void DeriveCodes(EgressWorkItem item)
    {
        item.CorrectionType = EgressOutputTypes.CorrectionType(item.Source.OutputType);
        if (item.Source.OutputType != EgressOutputType.RemoveLearners) return;

        var reason = item.Source.Answer("reason");
        var code = CorrectionCodes.RemoveReasonCode(reason);
        if (code is null)
            item.Fail(StepCodes, "Correction_Reason", $"No LDS correction reason code is defined for removal reason '{reason ?? "(none)"}'");
        item.CorrectionReason = code;
    }

    // S4: steps 2-4 are independent of one another (a bad correction reason says nothing about
    // whether the LAESTAB is valid), so none of them gates on HasFailed — only Build/Trim/Validate
    // do, once every independent check has had its turn. A record with two unrelated faults lists
    // both instead of ops discovering the second one only on the next run.
    // The pupil record's LAESTAB first (a real pupil always has one), then the school's from the
    // request row (the only source for a new learner, whose synthetic pupil has none).
    public static void SplitEstablishment(EgressWorkItem item)
    {
        var raw = !string.IsNullOrWhiteSpace(item.Source.PupilLaestab) ? item.Source.PupilLaestab : item.Source.OrganisationLaestab;
        if (LaestabSplitter.TrySplit(raw, out var la, out var estab))
        {
            item.LocalAuthority = la;
            item.Establishment = estab;
            return;
        }
        item.Fail(StepSplit, "Establishment_Number",
            string.IsNullOrWhiteSpace(raw)
                ? "No 7-digit DfE establishment number is held for this request or its school"
                : $"DfE establishment number '{raw}' is not 7 digits");
    }

    public static void StandardiseDates(EgressWorkItem item)
    {
        var dobRaw = item.Source.OutputType == EgressOutputType.NewLearners
            ? item.Source.Answer("date-of-birth") ?? item.Source.PupilDateOfBirth
            : item.Source.PupilDateOfBirth;
        if (EgressDates.TryToIso(dobRaw, out var dob)) item.DateOfBirthIso = dob;
        else item.Fail(StepDates, "Date_of_Birth", $"Date of birth '{dobRaw ?? "(none)"}' is not a recognisable date");

        if (item.Source.OutputType == EgressOutputType.NewLearners)
        {
            var admissionRaw = item.Source.Answer("admission-date");
            if (EgressDates.TryToIso(admissionRaw, out var admission)) item.AdmissionDateIso = admission;
            else item.Fail(StepDates, "Admission_Date", $"Admission date '{admissionRaw ?? "(none)"}' is not a recognisable date");
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
        var stage = EgressOutputTypes.StageToken(s.WindowType);
        var ticket = s.TicketId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        var cycleYear = s.SubmittedAtUtc.Year.ToString(CultureInfo.InvariantCulture);
        var cycleMonth = s.SubmittedAtUtc.Month.ToString(CultureInfo.InvariantCulture);

        if (s.OutputType == EgressOutputType.RemoveLearners)
        {
            item.RemoveRow = new RemoveLearnerRow(
                CorrectionId: ticket, CorrectionType: item.CorrectionType ?? string.Empty, CorrectionReason: item.CorrectionReason ?? string.Empty,
                KeyStage: stage, EstablishmentNumber: item.Establishment, Surname: s.PupilSurname ?? string.Empty, Forename: s.PupilFirstname ?? string.Empty,
                Sex: (s.PupilSex ?? string.Empty).ToUpperInvariant(), DateOfBirth: item.DateOfBirthIso, CycleYear: cycleYear, CycleMonth: cycleMonth,
                LocalAuthority: item.LocalAuthority,
                LearnerId: s.PupilMatchRef > 0 ? s.PupilMatchRef.ToString(CultureInfo.InvariantCulture) : string.Empty,
                ChangeRequestId: s.ChangeRequestId, TicketId: s.TicketId, ReferenceNumber: s.ReferenceNumber);
            return;
        }

        // A new learner's identity comes from the typed journey answers (docs/add-pupil-journey.md);
        // the synthetic pupil mirrors them but the answers are the record of what the school typed.
        item.NewRow = new NewLearnerRow(
            CorrectionId: ticket, CorrectionType: item.CorrectionType ?? string.Empty, KeyStage: stage,
            LocalAuthority: item.LocalAuthority, EstablishmentNumber: item.Establishment,
            Surname: s.Answer("last-name") ?? s.PupilSurname ?? string.Empty, MiddleName: string.Empty,
            Forename: s.Answer("first-name") ?? s.PupilFirstname ?? string.Empty,
            Sex: (s.Answer("sex") ?? s.PupilSex ?? string.Empty).ToUpperInvariant(),
            DateOfBirth: item.DateOfBirthIso, AdmissionDate: item.AdmissionDateIso, Postcode: string.Empty,
            CycleYear: cycleYear, CycleMonth: cycleMonth, SchoolUrn: s.OrganisationUrn.ToString(CultureInfo.InvariantCulture),
            Uln: s.WindowType == CheckingWindowType.Post16 ? s.PupilIdentifier ?? string.Empty : string.Empty,
            Upn: (s.Answer("upn") ?? string.Empty).ToUpperInvariant(),
            LearnerId: s.PupilMatchRef > 0 ? s.PupilMatchRef.ToString(CultureInfo.InvariantCulture) : string.Empty,
            YearGroup: s.Answer("year-group") ?? string.Empty, SenStatus: s.Answer("sen-status") ?? string.Empty,
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
                LocalAuthority = r.LocalAuthority.Trim(), LearnerId = r.LearnerId.Trim()
            };
        if (item.NewRow is { } n)
            item.NewRow = n with
            {
                CorrectionId = n.CorrectionId.Trim(), CorrectionType = n.CorrectionType.Trim(), KeyStage = n.KeyStage.Trim(),
                LocalAuthority = n.LocalAuthority.Trim(), EstablishmentNumber = n.EstablishmentNumber.Trim(), Surname = n.Surname.Trim(),
                MiddleName = n.MiddleName.Trim(), Forename = n.Forename.Trim(), Sex = n.Sex.Trim(), DateOfBirth = n.DateOfBirth.Trim(),
                AdmissionDate = n.AdmissionDate.Trim(), Postcode = n.Postcode.Trim(), CycleYear = n.CycleYear.Trim(), CycleMonth = n.CycleMonth.Trim(),
                SchoolUrn = n.SchoolUrn.Trim(), Uln = n.Uln.Trim(), Upn = n.Upn.Trim(), LearnerId = n.LearnerId.Trim(),
                YearGroup = n.YearGroup.Trim(), SenStatus = n.SenStatus.Trim()
            };
    }
}
