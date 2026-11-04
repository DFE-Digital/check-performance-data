using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.Egress;

// Steps 2-6 of the pipeline on one record. A failure is recorded against the step and field that
// found it and the record carries on, so the ops user sees EVERY problem in one run. The Zendesk
// ticket is the truth: each column that has a Zendesk field is read from the ticket.
public sealed class EgressRecordBuilderTests
{
    private static Dictionary<string, string> RemoveTicket(string? reason = "4_31", string? laestab = "8604070", string dob = "2010-09-07") =>
        Ticket(new()
        {
            [ZendeskTicketFieldConstants.CorrectionTypeName] = "31_",
            [ZendeskTicketFieldConstants.CorrectionReason31Name] = reason,
            [ZendeskTicketFieldConstants.KeyStageName] = "ks4",
            [ZendeskTicketFieldConstants.DfeEstablishmentNumberName] = laestab,
            [ZendeskTicketFieldConstants.SurnameCypmdName] = "BELLINGHAM",
            [ZendeskTicketFieldConstants.ForenameCypmdName] = " JUDE ",
            [ZendeskTicketFieldConstants.SexName] = "m",
            [ZendeskTicketFieldConstants.DateOfBirthCypmdName] = dob,
            [ZendeskTicketFieldConstants.CycleYearName] = "2026",
            [ZendeskTicketFieldConstants.CycleMonthName] = "6",
            [ZendeskTicketFieldConstants.LdsMatchedPupilIdName] = "555"
        });

    private static EgressSourceRecord Remove(Dictionary<string, string>? ticket = null) => new()
    {
        ChangeRequestId = Guid.Parse("11111111-1111-1111-1111-111111111111"), ReferenceNumber = "REF-R", TicketId = 88856, Decision = "approved",
        OutputType = EgressOutputType.RemoveLearners, WindowType = CheckingWindowType.KS4June,
        SubmittedAtUtc = new DateTime(2026, 6, 5, 9, 0, 0, DateTimeKind.Utc), OrganisationUrn = 142313, OrganisationLaestab = "860/4070",
        PupilFirstname = "Jude", PupilSurname = "Bellingham", PupilSex = "M", PupilDateOfBirth = "07/09/2010", PupilIdentifier = "A860407000011",
        PupilMatchRef = 555, PupilLaestab = "8604070", JourneyFound = true,
        Answers = new Dictionary<string, string> { ["reason"] = "pupil-died" },
        TicketFields = ticket ?? RemoveTicket()
    };

    private static EgressSourceRecord Add() => new()
    {
        ChangeRequestId = Guid.Parse("22222222-2222-2222-2222-222222222222"), ReferenceNumber = "REF-A", TicketId = 69390, Decision = "approved",
        OutputType = EgressOutputType.NewLearners, WindowType = CheckingWindowType.KS2,
        SubmittedAtUtc = new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc), OrganisationUrn = 136412, OrganisationLaestab = "860/4070",
        PupilFirstname = "Annie", PupilSurname = "Lennox", PupilSex = "F", PupilDateOfBirth = "07/09/2010", PupilIdentifier = "", PupilLaestab = "", JourneyFound = true,
        Answers = new Dictionary<string, string>
        {
            ["first-name"] = "Annie", ["last-name"] = "Lennox", ["date-of-birth"] = "2010-09-07", ["sex"] = "F", ["upn"] = "A881541200011",
            ["admission-date"] = "2018-09-04", ["year-group"] = "6", ["sen-status"] = "N"
        },
        TicketFields = Ticket(new()
        {
            [ZendeskTicketFieldConstants.CorrectionTypeName] = "10_",
            [ZendeskTicketFieldConstants.KeyStageName] = "ks2",
            [ZendeskTicketFieldConstants.DfeEstablishmentNumberName] = "8604070",
            [ZendeskTicketFieldConstants.SurnameCypmdName] = "LENNOX",
            [ZendeskTicketFieldConstants.ForenameCypmdName] = "ANNIE",
            [ZendeskTicketFieldConstants.SexName] = "f",
            [ZendeskTicketFieldConstants.DateOfBirthCypmdName] = "2010-09-07",
            [ZendeskTicketFieldConstants.AdmissionDateName] = "2018-09-04",
            [ZendeskTicketFieldConstants.CycleYearName] = "2026",
            [ZendeskTicketFieldConstants.CycleMonthName] = "10",
            [ZendeskTicketFieldConstants.SchoolUrnName] = "136412",
            [ZendeskTicketFieldConstants.UpnName] = "a881541200011"
        })
    };

    private static Dictionary<string, string> Ticket(Dictionary<string, string?> values) =>
        values.Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value!);

    private static Dictionary<string, string> With(Dictionary<string, string> ticket, string field, string? value)
    {
        var copy = new Dictionary<string, string>(ticket);
        if (value is null) copy.Remove(field);
        else copy[field] = value;
        return copy;
    }

    private static EgressWorkItem Run(EgressSourceRecord source)
    {
        var item = new EgressWorkItem(source);
        EgressRecordBuilder.ReadCodes(item);
        EgressRecordBuilder.SplitEstablishment(item);
        EgressRecordBuilder.StandardiseDates(item);
        EgressRecordBuilder.Build(item);
        EgressRecordBuilder.Trim(item);
        return item;
    }

    [Fact]
    public void A_remove_record_becomes_a_spec_row_from_the_ticket()
    {
        var item = Run(Remove());
        Assert.Empty(item.Failures);
        Assert.Equal(new RemoveLearnerRow("88856", "31", "4", "KS4", "4070", "BELLINGHAM", "JUDE", "M", "2010-09-07", "2026", "6", "860", "555",
            Guid.Parse("11111111-1111-1111-1111-111111111111"), 88856, "REF-R"), item.RemoveRow);
    }

    [Fact]
    public void A_new_learner_record_becomes_a_spec_row_from_the_ticket_and_the_journeys_year_group()
    {
        var item = Run(Add());
        Assert.Empty(item.Failures);
        Assert.Equal(new NewLearnerRow("69390", "10", "KS2", "860", "4070", "LENNOX", "ANNIE", "F", "2010-09-07", "2018-09-04", "", "2026", "10",
            "136412", "", "A881541200011", "", "6", Guid.Parse("22222222-2222-2222-2222-222222222222"), 69390, "REF-A"), item.NewRow);
    }

    // An agent can change a value on a scrutiny ticket. Egress sends what the ticket says, not
    // what CYPMD's own data would give.
    [Fact]
    public void The_ticket_wins_over_cypmd_data()
    {
        var ticket = With(RemoveTicket(reason: "8_31"), ZendeskTicketFieldConstants.SurnameCypmdName, "SMITH");
        var item = Run(Remove(ticket));
        Assert.Empty(item.Failures);
        Assert.Equal("8", item.RemoveRow!.CorrectionReason);
        Assert.Equal("SMITH", item.RemoveRow.Surname);
    }

    [Theory]
    [InlineData(ZendeskTicketFieldConstants.CorrectionTypeName, "Correction_Type")]
    [InlineData(ZendeskTicketFieldConstants.CorrectionReason31Name, "Correction_Reason")]
    public void A_missing_correction_code_fails_the_codes_step(string fieldName, string column)
    {
        var item = Run(Remove(With(RemoveTicket(), fieldName, null)));
        var failure = Assert.Single(item.Failures);
        Assert.Equal(EgressRecordBuilder.StepCodes, failure.Step);
        Assert.Equal(column, failure.Field);
        Assert.Contains(fieldName, failure.Reason);
        Assert.Null(item.RemoveRow);
    }

    [Fact]
    public void A_missing_establishment_number_fails_the_split_step_with_the_field_named()
    {
        var item = Run(Remove(RemoveTicket(laestab: null)));
        var failure = Assert.Single(item.Failures);
        Assert.Equal(EgressRecordBuilder.StepSplit, failure.Step);
        Assert.Equal("Establishment_Number", failure.Field);
        Assert.Equal(88856, failure.TicketId);
        Assert.Null(item.RemoveRow);
    }

    [Fact]
    public void An_establishment_number_that_is_not_7_digits_fails_the_split_step()
    {
        var failure = Assert.Single(Run(Remove(RemoveTicket(laestab: "86040"))).Failures);
        Assert.Equal(EgressRecordBuilder.StepSplit, failure.Step);
        Assert.Contains("not 7 digits", failure.Reason);
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
        var item = Run(Remove(RemoveTicket(dob: "2010-02-31")));
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
        var item = Run(Remove(RemoveTicket(reason: null, laestab: null)));
        Assert.Equal(2, item.Failures.Count);
        Assert.Contains(item.Failures, f => f.Step == EgressRecordBuilder.StepCodes && f.Field == "Correction_Reason");
        Assert.Contains(item.Failures, f => f.Step == EgressRecordBuilder.StepSplit && f.Field == "Establishment_Number");
        Assert.Null(item.RemoveRow);
    }

    // The build step lists each missing ticket value, not only the first one.
    [Fact]
    public void Each_missing_ticket_value_in_the_build_step_is_listed()
    {
        var ticket = With(With(RemoveTicket(), ZendeskTicketFieldConstants.CycleYearName, null), ZendeskTicketFieldConstants.KeyStageName, null);
        var item = Run(Remove(ticket));
        Assert.Equal(["Cycle_Year", "Key_Stage"], item.Failures.Select(f => f.Field).Order());
        Assert.All(item.Failures, f => Assert.Equal(EgressRecordBuilder.StepBuild, f.Step));
        Assert.Null(item.RemoveRow);
    }

    [Theory]
    [InlineData("ks2", "KS2")]
    [InlineData("ks4", "KS4")]
    [InlineData("16-18", "16-19")]
    public void Key_stage_is_the_ticket_option_as_the_LDS_value(string option, string expected)
    {
        var item = Run(Remove(With(RemoveTicket(), ZendeskTicketFieldConstants.KeyStageName, option)));
        Assert.Equal(expected, item.RemoveRow!.KeyStage);
    }

    [Fact]
    public void A_key_stage_option_that_LDS_does_not_take_fails_the_build_step()
    {
        var failure = Assert.Single(Run(Remove(With(RemoveTicket(), ZendeskTicketFieldConstants.KeyStageName, "ks3"))).Failures);
        Assert.Equal("Key_Stage", failure.Field);
        Assert.Contains("ks3", failure.Reason);
    }

    // The Zendesk "Sex" field has only m and f, so a pupil of unknown sex has no ticket value.
    [Fact]
    public void A_pupil_of_unknown_sex_takes_U_from_cypmd_when_the_ticket_has_no_sex()
    {
        var item = Run(Remove(With(RemoveTicket(), ZendeskTicketFieldConstants.SexName, null)) with { PupilSex = "U" });
        Assert.Empty(item.Failures);
        Assert.Equal("U", item.RemoveRow!.Sex);
    }

    [Fact]
    public void A_missing_sex_for_a_pupil_of_known_sex_fails_the_build_step()
    {
        var failure = Assert.Single(Run(Remove(With(RemoveTicket(), ZendeskTicketFieldConstants.SexName, null))).Failures);
        Assert.Equal("Sex", failure.Field);
    }

    [Fact]
    public void A_missing_learner_id_leaves_the_column_blank_for_the_validator_to_judge()
    {
        var item = Run(Remove(With(RemoveTicket(), ZendeskTicketFieldConstants.LdsMatchedPupilIdName, null)));
        Assert.Empty(item.Failures);
        Assert.Equal("", item.RemoveRow!.LearnerId);
    }

    [Fact]
    public void A_KS4_year_group_change_carries_the_tickets_year_group()
    {
        var item = Run(Remove(With(RemoveTicket(reason: "17_31"), ZendeskTicketFieldConstants.PupilYearGroupCypmdName, "9")));
        Assert.Empty(item.Failures);
        Assert.Equal("17", item.RemoveRow!.CorrectionReason);
        Assert.Equal("9", item.RemoveRow.YearGroup);
    }

    [Fact]
    public void A_KS4_removal_with_no_year_group_on_the_ticket_leaves_Year_Group_blank()
        => Assert.Equal("", Run(Remove()).RemoveRow!.YearGroup);

    [Fact]
    public void A_16_19_removal_maps_the_chosen_academic_years_onto_Removal_Year_0_to_2()
    {
        var ticket = With(With(RemoveTicket(reason: "329_31"), ZendeskTicketFieldConstants.KeyStageName, "16-18"), ZendeskTicketFieldConstants.CycleMonthName, "10");
        var source = Remove(ticket) with
        {
            WindowType = CheckingWindowType.Post16,
            Answers = new Dictionary<string, string> { ["reason"] = "other", ["years-to-remove"] = "2025-2026|2023-2024" }
        };
        var item = Run(source);
        Assert.Empty(item.Failures);
        Assert.Equal("16-19", item.RemoveRow!.KeyStage);
        Assert.Equal("TRUE", item.RemoveRow.RemovalYear0);    // 2025-2026
        Assert.Equal("FALSE", item.RemoveRow.RemovalYear1);   // 2024-2025
        Assert.Equal("TRUE", item.RemoveRow.RemovalYear2);    // 2023-2024
        Assert.Equal("", item.RemoveRow.YearGroup);
        Assert.Equal("329", item.RemoveRow.CorrectionReason);
    }

    [Fact]
    public void A_16_19_removal_whose_journey_did_not_ask_about_years_leaves_them_blank()
    {
        var item = Run(Remove(With(RemoveTicket(), ZendeskTicketFieldConstants.KeyStageName, "16-18")) with { WindowType = CheckingWindowType.Post16 });
        Assert.Equal(("", "", ""), (item.RemoveRow!.RemovalYear0, item.RemoveRow.RemovalYear1, item.RemoveRow.RemovalYear2));
    }
}
