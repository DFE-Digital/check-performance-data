using System.Globalization;
using System.Text;
using DfE.CheckPerformanceData.Application.Dashboard;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using Options = DfE.CheckPerformanceData.Application.ZendeskClient.ZendeskTicketFieldOptions;
using Names = DfE.CheckPerformanceData.Application.ZendeskClient.ZendeskTicketFieldConstants;

namespace DfE.CheckPerformanceData.RulesEngineWorker.Consumers;

/// <summary>
/// Builds the Zendesk ticket for a results enquiry (#859). The ticket sends only the fields that
/// the Results Enquiry DCI form needs, so that it lands in the correct Zendesk view. It does not
/// send the pupil-amendment fields (decision, cycle, correction). See docs/results-enquiry.md.
/// </summary>
/// <remarks>
/// The values come from <see cref="RequestDocument.Enquiry"/> and from the answers, by
/// <see cref="QuestionRole"/> and <see cref="OptionMeaning"/>. This class names no flow id.
/// A field whose id is not set (or is 0) is left out, as for every ticket.
/// </remarks>
internal sealed class ResultsEnquiryTicketBuilder(
    IZendeskTicketFieldService? ticketFieldService,
    SchoolCheckingExerciseSettings? settings,
    ILogger logger)
{
    public CreateTicketRequestDto Build(RequestDocument message)
    {
        var enquiry = message.Enquiry
            ?? throw new ArgumentException("The message has no enquiry part.", nameof(message));

        var dto = new CreateTicketRequestDto
        {
            Ticket = new CreateTicketDto
            {
                Subject = $"{message.RequestTypeCode} ({message.ReferenceNumber})",
                Comment = CreateTicketCommentDto.InternalNote(BuildDescription(message)),
                Status = "new",
                Type = "question",
                Priority = "low",
                BrandId = settings?.BrandId ?? 0,
                // Every enquiry goes to the main group, never the Scrutiny group: the enquiry
                // views accept only the main group.
                GroupId = settings?.GroupId ?? 0,
                TicketFormId = settings?.ResultsEnquiryTicketFormId is > 0 ? settings.ResultsEnquiryTicketFormId : null,
            }
        };

        if (ticketFieldService is null)
        {
            return dto;
        }

        // Routing fields. The Zendesk views filter on these.
        Add(dto, Names.SituationCypmdName, Situation(enquiry.Type));
        Add(dto, Names.KeyStageName, ticketFieldService.GetOptionValue(Names.KeyStageName, message.CheckingWindowType));
        Add(dto, Names.GradedExamCypmdName,
            GradedExamQualifications.ForEnquiry(WindowType(message), enquiry.Qan) == true
                ? Options.ResultsEnquiry.GradedExam
                : Options.ResultsEnquiry.NotGradedExam);
        Add(dto, Names.TLevelExamsCypmdName,
            TLevelQualifications.Contains(enquiry.Qan) ? Options.ResultsEnquiry.TLevel : Options.ResultsEnquiry.NotTLevel);
        Add(dto, Names.WithdrawalQualificationCypmdName, Withdrawal(enquiry.Type, message));
        Add(dto, Names.TypeOfOrganisationName, Options.TypeOfOrganisation.TrainingProvider);

        // The school and the user who submitted the enquiry.
        Add(dto, Names.CustomerNameCypmdName, message.SubmittedBy.DisplayName);
        Add(dto, Names.EmailAddressCypmdName, message.SubmittedBy.Email);
        Add(dto, Names.SchoolNameCypmdName, message.School.Name);
        AddInteger(dto, Names.DfeNumberCypmdName, LaestabNormaliser.Normalise(message.School.Laestab), message);
        Add(dto, Names.DciRefCypmdName, message.ReferenceNumber);

        // The pupil.
        AddInteger(dto, Names.CypmdName, message.Pupil.CypmdId, message);
        AddInteger(dto, Names.LdsMatchedPupilIdName,
            message.Pupil.MatchRef > 0 ? message.Pupil.MatchRef.ToString(CultureInfo.InvariantCulture) : null, message);
        Add(dto, Names.ForenameCypmdName, message.Pupil.Firstname.ToUpperInvariant());
        Add(dto, Names.SurnameCypmdName, message.Pupil.Surname.ToUpperInvariant());
        Add(dto, Names.DateOfBirthCypmdName, IsoDate(message.Pupil.DateOfBirth));
        AddInteger(dto, Names.AgeCypmdName,
            message.Pupil.Age > 0 ? message.Pupil.Age.ToString(CultureInfo.InvariantCulture) : null, message);

        // The qualification and result (#861, #862).
        Add(dto, Names.QanCypmdName, enquiry.Qan);
        Add(dto, Names.QualificationNameCypmdName, enquiry.QualificationName);
        Add(dto, Names.AoNameCypmdName, enquiry.AwardingOrganisation);
        Add(dto, Names.SyllabusCodeCypmdName, enquiry.SyllabusCode);
        Add(dto, Names.CurrentGradeCypmdName, enquiry.CurrentGrade);

        // The school's answers, by role (#863).
        var cohort = Answer(message, QuestionRole.EnquiryCohortScope)?.Meaning;
        Add(dto, Names.WholeCohortAffectedCypmdName, cohort switch
        {
            OptionMeaning.Yes => Options.ResultsEnquiry.WholeCohort,
            OptionMeaning.No => Options.ResultsEnquiry.NotWholeCohort,
            _ => null
        });
        if (cohort == OptionMeaning.Yes)
        {
            // The count question is on the path only when the whole cohort is affected.
            Add(dto, Names.NumberOfQualificationsCypmdName, RawValue(message, QuestionRole.EnquiryCohortCount));
        }

        Add(dto, Names.AwardDateCypmdName, RawValue(message, QuestionRole.EnquiryAwardDate));
        Add(dto, Names.RevisedGradeCypmdName, RawValue(message, QuestionRole.EnquiryGrade));
        AddInteger(dto, Names.NcnCypmdName, RawValue(message, QuestionRole.EnquiryNcn), message);
        Add(dto, Names.AdditionalCommentsCypmdName, RawValue(message, QuestionRole.EnquiryAdditionalComments));

        return dto;
    }

    // The Situation tag for each enquiry type. Another type cannot have an enquiry part, so it
    // stops the ticket: a ticket without a Situation lands in no view.
    private static string Situation(WhatToChange type) => type switch
    {
        WhatToChange.MissingQualification => Options.ResultsEnquiry.Situation.MissingQualification,
        WhatToChange.IncorrectGrade => Options.ResultsEnquiry.Situation.IncorrectGrade,
        WhatToChange.ResultDoesNotBelong => Options.ResultsEnquiry.Situation.ResultDoesNotBelong,
        _ => throw new InvalidOperationException($"{type} is not a results enquiry type.")
    };

    // The Withdrawal qualification tag (#908). An Incorrect grade or Result does not belong
    // enquiry is "yes" when the additional information contains a form of "withdraw". The stem
    // "withdr" also matches "withdrew" and the misspelling "withdrawl". A false positive, such as
    // "not withdrawn", is acceptable: an operator moves the ticket by hand.
    private static string Withdrawal(WhatToChange type, RequestDocument message) =>
        type is WhatToChange.IncorrectGrade or WhatToChange.ResultDoesNotBelong
        && (RawValue(message, QuestionRole.EnquiryAdditionalComments)?.Contains(WithdrawalStem, StringComparison.OrdinalIgnoreCase) ?? false)
            ? Options.ResultsEnquiry.Withdrawn
            : Options.ResultsEnquiry.NotWithdrawn;

    private const string WithdrawalStem = "withdr";

    // TryParse also accepts a number, a comma list and outer spaces. Only an exact name counts.
    private static CheckingWindowType? WindowType(RequestDocument message) =>
        Enum.TryParse<CheckingWindowType>(message.CheckingWindowType, out var type)
        && type.ToString() == message.CheckingWindowType
            ? type
            : null;

    private static AnswerRecord? Answer(RequestDocument message, QuestionRole role) =>
        message.Answers.FirstOrDefault(a => a.Role == role);

    private static string? RawValue(RequestDocument message, QuestionRole role) =>
        Answer(message, role)?.RawValue?.Trim();

    // The pupil's date of birth is dd/MM/yyyy. The Zendesk date field needs yyyy-MM-dd.
    private static string? IsoDate(string? value) =>
        DateTime.TryParseExact(value, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;

    private void Add(CreateTicketRequestDto dto, string fieldName, string? value)
    {
        var fieldId = ticketFieldService?.GetFieldIdFromConfig(fieldName);
        if (fieldId is not > 0 || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        dto.Ticket.CustomFields.Add(new CustomFieldDto { Id = fieldId, Value = value });
    }

    // An integer field. Zendesk rejects the whole ticket (422) for a value that is not an integer,
    // so such a value is left out and logged. The log names the field, not the value.
    private void AddInteger(CreateTicketRequestDto dto, string fieldName, string? value, RequestDocument message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!long.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            logger.LogWarning(
                "Results enquiry {Reference}: {Field} is not an integer. The ticket leaves it out.",
                message.ReferenceNumber, fieldName);
            return;
        }

        Add(dto, fieldName, value.Trim());
    }

    private static string BuildDescription(RequestDocument message)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{message.RequestTypeCode} {message.ReferenceNumber} for window {message.CheckingWindowId} ({message.CheckingWindowType}).");
        sb.AppendLine();

        var answers = message.Answers.Where(a => !string.IsNullOrWhiteSpace(a.Value)).ToList();
        if (answers.Count > 0)
        {
            sb.AppendLine("Answers:");
            foreach (var answer in answers)
            {
                sb.AppendLine($"  - {answer.QuestionTitle}: {answer.Value}");
            }
        }

        return sb.ToString();
    }
}
