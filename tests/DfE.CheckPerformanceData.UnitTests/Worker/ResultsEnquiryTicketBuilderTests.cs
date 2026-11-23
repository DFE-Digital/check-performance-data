using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using DfE.CheckPerformanceData.RulesEngineWorker.Consumers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Names = DfE.CheckPerformanceData.Application.ZendeskClient.ZendeskTicketFieldConstants;

namespace DfE.CheckPerformanceData.Application.UnitTests.Worker;

/// <summary>
/// The results enquiry ticket (#859). The Zendesk views route a ticket on its form, group and
/// routing fields, so these tests check each value that a view or a DCI agent reads.
/// </summary>
public sealed class ResultsEnquiryTicketBuilderTests
{
    private const string Reference = "CYPMD_16to19_RE_4F9C2A1";
    private const string TLevelQan = "60369115";

    private readonly ZendeskTicketFieldSettings _fieldIds = AllFieldIdsSet();
    private readonly ILogger _logger = Substitute.For<ILogger>();
    private readonly SchoolCheckingExerciseSettings _settings = new()
    {
        TargetViewTitle = "School Checking Exercise",
        BrandId = 1234,
        GroupId = 5678,
        ScrutinyGroupId = 9012,
        ResultsEnquiryTicketFormId = 4321,
    };

    private CreateTicketDto Build(RequestDocument message) =>
        new ResultsEnquiryTicketBuilder(
                new ZendeskTicketFieldService(_fieldIds, Substitute.For<IZendeskApi>(), Substitute.For<ILogger<ZendeskTicketFieldService>>()),
                _settings, _logger)
            .Build(message).Ticket;

    // ── Ticket settings ─────────────────────────────────────────────────────

    [Fact]
    public void The_ticket_uses_the_enquiry_form_the_main_group_and_low_priority()
    {
        var ticket = Build(Message(WhatToChange.IncorrectGrade));

        Assert.Equal(4321, ticket.TicketFormId);
        Assert.Equal(1234, ticket.BrandId);
        // Not the Scrutiny group: the enquiry views accept only the main group.
        Assert.Equal(5678, ticket.GroupId);
        Assert.Equal("new", ticket.Status);
        Assert.Equal("question", ticket.Type);
        Assert.Equal("low", ticket.Priority);
        Assert.Contains(Reference, ticket.Subject);
        Assert.Contains("Challenged result", ticket.Comment!.Body);
    }

    // A public comment from the API agent gets the agent signature, which looks like an email
    // to the school.
    [Fact]
    public void The_ticket_text_is_an_internal_note()
    {
        Assert.False(Build(Message(WhatToChange.IncorrectGrade)).Comment!.Public);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public void No_form_id_leaves_the_form_out(long? formId)
    {
        _settings.ResultsEnquiryTicketFormId = formId;

        Assert.Null(Build(Message(WhatToChange.IncorrectGrade)).TicketFormId);
    }

    // ── Routing fields ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(WhatToChange.MissingQualification, "missing_qualification_1")]
    [InlineData(WhatToChange.IncorrectGrade, "incorrect_grade")]
    [InlineData(WhatToChange.ResultDoesNotBelong, "query_result")]
    public void Situation_comes_from_the_enquiry_type(WhatToChange type, string expected)
    {
        Assert.Equal(expected, Field(Build(Message(type)), Names.SituationCypmdName));
    }

    [Fact]
    public void A_type_that_is_not_an_enquiry_stops_the_ticket()
    {
        Assert.Throws<InvalidOperationException>(() => Build(Message(WhatToChange.Remove)));
    }

    [Fact]
    public void The_fixed_routing_fields_have_their_16_to_19_values()
    {
        var ticket = Build(Message(WhatToChange.IncorrectGrade));

        Assert.Equal("16-18", Field(ticket, Names.KeyStageName));
        Assert.Equal("no_graded", Field(ticket, Names.GradedExamCypmdName));
        Assert.Equal("no_withdraw_cypmd", Field(ticket, Names.WithdrawalQualificationCypmdName));
        Assert.Equal("training_provider", Field(ticket, Names.TypeOfOrganisationName));
    }

    [Theory]
    [InlineData(TLevelQan, "yes_tlevel_cypmd")]
    [InlineData("60146084", "no_tlevel_cypmd")]
    public void T_level_comes_from_the_t_level_list(string qan, string expected)
    {
        Assert.Equal(expected, Field(Build(Message(WhatToChange.IncorrectGrade, qan: qan)), Names.TLevelExamsCypmdName));
    }

    // ── Withdrawal qualification (#908) ─────────────────────────────────────

    [Theory]
    [InlineData(WhatToChange.IncorrectGrade, "The student will withdraw from the course.")]
    [InlineData(WhatToChange.IncorrectGrade, "This qualification was withdrawn.")]
    [InlineData(WhatToChange.IncorrectGrade, "Withdrawal confirmed by the AO.")]
    [InlineData(WhatToChange.IncorrectGrade, "THE STUDENT WITHDREW IN MAY")]
    [InlineData(WhatToChange.IncorrectGrade, "withdrawl form sent")]
    [InlineData(WhatToChange.IncorrectGrade, "We are withdrawing the entry.")]
    [InlineData(WhatToChange.IncorrectGrade, "This was not withdrawn.")]
    [InlineData(WhatToChange.ResultDoesNotBelong, "Withdrawn before the exam.")]
    [InlineData(WhatToChange.ResultDoesNotBelong, "The student withdraws.")]
    public void A_form_of_withdraw_in_the_additional_information_sets_withdrawal_to_yes(WhatToChange type, string comments)
    {
        var ticket = Build(Message(type, answers: [Answer(QuestionRole.EnquiryAdditionalComments, comments)]));

        Assert.Equal("yes_withdraw_cypmd", Field(ticket, Names.WithdrawalQualificationCypmdName));
    }

    [Fact]
    public void A_missing_qualification_never_sets_withdrawal_to_yes()
    {
        var ticket = Build(Message(WhatToChange.MissingQualification,
            answers: [Answer(QuestionRole.EnquiryAdditionalComments, "This qualification was withdrawn.")]));

        Assert.Equal("no_withdraw_cypmd", Field(ticket, Names.WithdrawalQualificationCypmdName));
    }

    [Theory]
    [InlineData(WhatToChange.IncorrectGrade)]
    [InlineData(WhatToChange.ResultDoesNotBelong)]
    public void No_additional_information_sets_withdrawal_to_no(WhatToChange type)
    {
        Assert.Equal("no_withdraw_cypmd", Field(Build(Message(type)), Names.WithdrawalQualificationCypmdName));
    }

    [Fact]
    public void Additional_information_without_the_word_sets_withdrawal_to_no()
    {
        var ticket = Build(Message(WhatToChange.IncorrectGrade,
            answers: [Answer(QuestionRole.EnquiryAdditionalComments, "The AO confirmed the grade.")]));

        Assert.Equal("no_withdraw_cypmd", Field(ticket, Names.WithdrawalQualificationCypmdName));
    }

    [Fact]
    public void The_t_level_list_has_the_21_qans_from_dci()
    {
        Assert.Equal(21, TLevelQualifications.All.Count);
        Assert.True(TLevelQualifications.Contains("6037066x"));
    }

    // ── Data fields ─────────────────────────────────────────────────────────

    [Fact]
    public void The_school_user_pupil_and_qualification_fields_are_sent()
    {
        var ticket = Build(Message(WhatToChange.IncorrectGrade));

        Assert.Equal("Ada Editor", Field(ticket, Names.CustomerNameCypmdName));
        Assert.Equal("ada@school.test", Field(ticket, Names.EmailAddressCypmdName));
        Assert.Equal("Test School", Field(ticket, Names.SchoolNameCypmdName));
        Assert.Equal("8604070", Field(ticket, Names.DfeNumberCypmdName));
        Assert.Equal(Reference, Field(ticket, Names.DciRefCypmdName));
        Assert.Equal("1596410810", Field(ticket, Names.CypmdName));
        Assert.Equal("123456", Field(ticket, Names.LdsMatchedPupilIdName));
        Assert.Equal("BILLY", Field(ticket, Names.ForenameCypmdName));
        Assert.Equal("B", Field(ticket, Names.SurnameCypmdName));
        Assert.Equal("2007-03-12", Field(ticket, Names.DateOfBirthCypmdName));
        Assert.Equal("19", Field(ticket, Names.AgeCypmdName));
        Assert.Equal("60146084", Field(ticket, Names.QanCypmdName));
        Assert.Equal("AQA GCSE Mathematics", Field(ticket, Names.QualificationNameCypmdName));
        Assert.Equal("AQA", Field(ticket, Names.AoNameCypmdName));
        Assert.Equal("8300H", Field(ticket, Names.SyllabusCodeCypmdName));
        Assert.Equal("5", Field(ticket, Names.CurrentGradeCypmdName));
    }

    [Fact]
    public void An_empty_value_leaves_its_field_out()
    {
        // A missing qualification has no current grade. A result whose QAN is not in the
        // reference has no AO name (#862).
        var ticket = Build(Message(WhatToChange.MissingQualification, currentGrade: "", ao: ""));

        Assert.False(HasField(ticket, Names.CurrentGradeCypmdName));
        Assert.False(HasField(ticket, Names.AoNameCypmdName));
    }

    [Fact]
    public void The_ticket_leaves_out_the_pupil_amendment_and_matching_fields()
    {
        var ticket = Build(Message(WhatToChange.IncorrectGrade));

        foreach (var name in new[]
                 {
                     Names.DecisionStatusName, Names.CorrectionTypeName, Names.CycleYearName, Names.CycleMonthName,
                     Names.ReasonForRemovalName, Names.UpnName, Names.SexName, Names.DfeEstablishmentNumberName
                 })
        {
            Assert.False(HasField(ticket, name), name);
        }
    }

    // ── Answers, by role ────────────────────────────────────────────────────

    [Fact]
    public void A_whole_cohort_answer_sends_yes_and_the_number_of_qualifications()
    {
        var ticket = Build(Message(WhatToChange.MissingQualification, answers:
        [
            Answer(QuestionRole.EnquiryCohortScope, "yes", OptionMeaning.Yes),
            Answer(QuestionRole.EnquiryCohortCount, "12"),
        ]));

        Assert.Equal("yes_re", Field(ticket, Names.WholeCohortAffectedCypmdName));
        Assert.Equal("12", Field(ticket, Names.NumberOfQualificationsCypmdName));
    }

    [Fact]
    public void A_single_student_answer_sends_no_and_no_number()
    {
        var ticket = Build(Message(WhatToChange.IncorrectGrade, answers:
        [
            Answer(QuestionRole.EnquiryCohortScope, "no", OptionMeaning.No),
            Answer(QuestionRole.EnquiryCohortCount, "12"),
        ]));

        Assert.Equal("no_re", Field(ticket, Names.WholeCohortAffectedCypmdName));
        Assert.False(HasField(ticket, Names.NumberOfQualificationsCypmdName));
    }

    [Fact]
    public void Result_does_not_belong_has_no_cohort_answer_so_no_cohort_field()
    {
        var ticket = Build(Message(WhatToChange.ResultDoesNotBelong));

        Assert.False(HasField(ticket, Names.WholeCohortAffectedCypmdName));
    }

    [Fact]
    public void The_other_answers_reach_their_fields_by_role()
    {
        var ticket = Build(Message(WhatToChange.MissingQualification, answers:
        [
            Answer(QuestionRole.EnquiryAwardDate, "2026-08-13"),
            Answer(QuestionRole.EnquiryGrade, "7"),
            Answer(QuestionRole.EnquiryNcn, "12345"),
            Answer(QuestionRole.EnquiryAdditionalComments, "  The AO confirmed the grade.  "),
            // A question with no role reaches no field.
            new AnswerRecord { QuestionId = "q-other", QuestionTitle = "Other", Type = "FreeText", RawValue = "999" },
        ]));

        Assert.Equal("2026-08-13", Field(ticket, Names.AwardDateCypmdName));
        Assert.Equal("7", Field(ticket, Names.RevisedGradeCypmdName));
        Assert.Equal("12345", Field(ticket, Names.NcnCypmdName));
        Assert.Equal("The AO confirmed the grade.", Field(ticket, Names.AdditionalCommentsCypmdName));
    }

    [Fact]
    public void An_ncn_that_is_not_an_integer_is_left_out_and_the_ticket_is_still_built()
    {
        // NCN is an integer field. Zendesk rejects the whole ticket for a value that is not one.
        var ticket = Build(Message(WhatToChange.MissingQualification, answers:
        [
            Answer(QuestionRole.EnquiryNcn, "12A45"),
        ]));

        Assert.False(HasField(ticket, Names.NcnCypmdName));
        Assert.Equal("missing_qualification_1", Field(ticket, Names.SituationCypmdName));
    }

    [Fact]
    public void A_field_with_no_id_is_left_out()
    {
        _fieldIds.SituationCypmdId = 0;

        var ticket = Build(Message(WhatToChange.IncorrectGrade));

        Assert.DoesNotContain(ticket.CustomFields, f => f.Id is null or 0);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    // Each field gets its own id, so a test can find a field by name.
    private static ZendeskTicketFieldSettings AllFieldIdsSet()
    {
        var settings = new ZendeskTicketFieldSettings();
        var next = 1000L;
        foreach (var property in typeof(ZendeskTicketFieldSettings).GetProperties().Where(p => p.PropertyType == typeof(long?)))
            property.SetValue(settings, next++);
        return settings;
    }

    private string? Field(CreateTicketDto ticket, string name)
    {
        var id = _fieldIds.GetFieldIdByName(name);
        Assert.NotNull(id);
        return ticket.CustomFields.SingleOrDefault(f => f.Id == id)?.Value?.ToString();
    }

    private bool HasField(CreateTicketDto ticket, string name) =>
        ticket.CustomFields.Any(f => f.Id == _fieldIds.GetFieldIdByName(name));

    private static AnswerRecord Answer(QuestionRole role, string rawValue, OptionMeaning? meaning = null) => new()
    {
        QuestionId = "q-" + role, QuestionTitle = role.ToString(), Type = "FreeText",
        Role = role, Meaning = meaning, Value = rawValue, RawValue = rawValue
    };

    private static RequestDocument Message(
        WhatToChange type,
        string qan = "60146084",
        string currentGrade = "5",
        string ao = "AQA",
        List<AnswerRecord>? answers = null) => new()
    {
        ChangeRequestId = Guid.NewGuid(),
        ReferenceNumber = Reference,
        SubmittedAt = DateTime.UtcNow,
        SubmittedBy = new UserDetails { UserId = "u", DisplayName = "Ada Editor", Email = "ada@school.test" },
        CheckingWindowId = Guid.NewGuid(),
        CheckingWindowType = "Post16",
        RequestTypeCode = "Results enquiry - Incorrect grade",
        School = new SchoolDetails { Urn = "142313", Name = "Test School", Laestab = "860/4070" },
        Pupil = new PupilDetails
        {
            Id = "p1", CypmdId = "1596410810", Firstname = "Billy", Surname = "B",
            DateOfBirth = "12/03/2007", Sex = "M", Age = 19, Upn = "UPN1", MatchRef = 123456
        },
        Answers = answers ??
        [
            new AnswerRecord
            {
                QuestionId = "challenged-result", QuestionTitle = "Challenged result", Type = "ResultsEnquiry",
                Value = "AQA GCSE Mathematics — QAN 60146084, syllabus 8300H, session S2024, held grade 5"
            }
        ],
        Enquiry = new EnquiryDetails
        {
            Type = type, Qan = qan, QualificationName = "AQA GCSE Mathematics", AwardingOrganisation = ao,
            SyllabusCode = "8300H", CurrentGrade = currentGrade
        }
    };
}
