using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.Egress;

// Steps 2-6 of the pipeline on one record. A failure is recorded against the step and field that
// found it and the record carries on, so the ops user sees EVERY problem in one run.
public sealed class EgressRecordBuilderTests
{
    private static EgressSourceRecord Remove(string? reason = "pupil-died", string? pupilLaestab = "8604070", string? orgLaestab = "860/4070", string dob = "07/09/2010", int matchRef = 555) => new()
    {
        ChangeRequestId = Guid.Parse("11111111-1111-1111-1111-111111111111"), ReferenceNumber = "REF-R", TicketId = 88856, Decision = "approved",
        OutputType = EgressOutputType.RemoveLearners, WindowType = CheckingWindowType.KS4June,
        SubmittedAtUtc = new DateTime(2026, 6, 5, 9, 0, 0, DateTimeKind.Utc), OrganisationUrn = 142313, OrganisationLaestab = orgLaestab,
        PupilFirstname = " Jude ", PupilSurname = "Bellingham", PupilSex = "M", PupilDateOfBirth = dob, PupilIdentifier = "A860407000011",
        PupilMatchRef = matchRef, PupilLaestab = pupilLaestab, JourneyFound = true,
        Answers = reason is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["reason"] = reason }
    };

    private static EgressSourceRecord Add(string? orgLaestab = "860/4070") => new()
    {
        ChangeRequestId = Guid.Parse("22222222-2222-2222-2222-222222222222"), ReferenceNumber = "REF-A", TicketId = 69390, Decision = "approved",
        OutputType = EgressOutputType.NewLearners, WindowType = CheckingWindowType.KS2,
        SubmittedAtUtc = new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc), OrganisationUrn = 136412, OrganisationLaestab = orgLaestab,
        PupilFirstname = "Annie", PupilSurname = "Lennox", PupilSex = "F", PupilDateOfBirth = "07/09/2010", PupilIdentifier = "", PupilLaestab = "", JourneyFound = true,
        Answers = new Dictionary<string, string>
        {
            ["first-name"] = "Annie", ["last-name"] = "Lennox", ["date-of-birth"] = "2010-09-07", ["sex"] = "F", ["upn"] = "A881541200011",
            ["admission-date"] = "2018-09-04", ["year-group"] = "6", ["sen-status"] = "N"
        }
    };

    private static EgressWorkItem Run(EgressSourceRecord source)
    {
        var item = new EgressWorkItem(source);
        EgressRecordBuilder.DeriveCodes(item);
        EgressRecordBuilder.SplitEstablishment(item);
        EgressRecordBuilder.StandardiseDates(item);
        EgressRecordBuilder.Build(item);
        EgressRecordBuilder.Trim(item);
        return item;
    }

    [Fact]
    public void A_remove_record_becomes_a_spec_row()
    {
        var item = Run(Remove());
        Assert.Empty(item.Failures);
        Assert.Equal(new RemoveLearnerRow("88856", "31", "4", "KS4", "4070", "Bellingham", "Jude", "M", "2010-09-07", "2026", "6", "860", "555",
            Guid.Parse("11111111-1111-1111-1111-111111111111"), 88856, "REF-R"), item.RemoveRow);
    }

    [Fact]
    public void A_new_learner_record_becomes_a_spec_row_from_the_journey_answers_and_the_schools_laestab()
    {
        var item = Run(Add());
        Assert.Empty(item.Failures);
        Assert.Equal(new NewLearnerRow("69390", "10", "KS2", "860", "4070", "Lennox", "", "Annie", "F", "2010-09-07", "2018-09-04", "", "2026", "10",
            "136412", "", "A881541200011", "", "6", "N", Guid.Parse("22222222-2222-2222-2222-222222222222"), 69390, "REF-A"), item.NewRow);
    }

    [Fact]
    public void Pupil_laestab_wins_over_the_request_rows_when_both_are_present()
    {
        var item = Run(Remove(pupilLaestab: "3735401", orgLaestab: "860/4070"));
        Assert.Equal("373", item.RemoveRow!.LocalAuthority);
        Assert.Equal("5401", item.RemoveRow.EstablishmentNumber);
    }

    [Fact]
    public void No_laestab_anywhere_fails_the_split_step_with_the_field_named()
    {
        var item = Run(Remove(pupilLaestab: "", orgLaestab: null));
        var failure = Assert.Single(item.Failures);
        Assert.Equal(EgressRecordBuilder.StepSplit, failure.Step);
        Assert.Equal("Establishment_Number", failure.Field);
        Assert.Equal(88856, failure.TicketId);
        Assert.Null(item.RemoveRow);
    }

    [Fact]
    public void An_unmapped_remove_reason_fails_the_codes_step()
    {
        var item = Run(Remove(reason: "other"));
        var failure = Assert.Single(item.Failures);
        Assert.Equal(EgressRecordBuilder.StepCodes, failure.Step);
        Assert.Equal("Correction_Reason", failure.Field);
        Assert.Contains("other", failure.Reason);
    }

    [Fact]
    public void A_missing_journey_fails_the_build_step()
    {
        var item = Run(Remove() with { JourneyFound = false });
        Assert.Contains(item.Failures, f => f.Step == EgressRecordBuilder.StepBuild && f.Reason.Contains("journey"));
    }

    [Fact]
    public void An_unparseable_date_fails_the_dates_step_and_later_steps_do_not_pile_on()
    {
        var item = Run(Remove(dob: "31/02/2010"));
        var failure = Assert.Single(item.Failures);
        Assert.Equal(EgressRecordBuilder.StepDates, failure.Step);
        Assert.Equal("Date_of_Birth", failure.Field);
    }

    // S4: steps 2-4 are independent of one another, so a fault in one must not hide a fault in
    // another — ops must see everything wrong with a record in one run, not discover the second
    // problem only after fixing the first and re-running.
    [Fact]
    public void Independent_faults_in_different_steps_are_all_listed_not_just_the_first()
    {
        var item = Run(Remove(reason: "other", pupilLaestab: "", orgLaestab: null));
        Assert.Equal(2, item.Failures.Count);
        Assert.Contains(item.Failures, f => f.Step == EgressRecordBuilder.StepCodes && f.Field == "Correction_Reason");
        Assert.Contains(item.Failures, f => f.Step == EgressRecordBuilder.StepSplit && f.Field == "Establishment_Number");
        Assert.Null(item.RemoveRow);
    }

    [Fact]
    public void Post16_reasons_and_stage_are_handled()
    {
        var item = Run(Remove(reason: "student-died") with { WindowType = CheckingWindowType.Post16 });
        Assert.Equal("KS5", item.RemoveRow!.KeyStage);
        Assert.Equal("4", item.RemoveRow.CorrectionReason);
    }

    [Fact]
    public void Cycle_month_is_not_zero_padded_and_cycle_year_is_four_digits()
    {
        var item = Run(Remove());
        Assert.Equal("6", item.RemoveRow!.CycleMonth);
        Assert.Equal("2026", item.RemoveRow.CycleYear);
    }
}
