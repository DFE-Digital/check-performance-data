using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public class RequestDocument
{
    /// <summary>
    /// Primary key of the <c>ChangeRequests</c> row this document was built from.
    /// The rules engine worker writes its decision back to that row by this Id.
    /// </summary>
    public required Guid ChangeRequestId { get; init; }

    public required string ReferenceNumber { get; init; }
    public DateTime SubmittedAt { get; init; }
    public required UserDetails SubmittedBy { get; init; }
    public Guid CheckingWindowId { get; init; }
    /// <summary>
    /// The checking window's start date. The Zendesk "Cycle year" and "Cycle month" fields come
    /// from it: LDS wants the month that the exercise takes place, not the month of submission.
    /// Null when not supplied: the worker then leaves the two fields out.
    /// </summary>
    public DateTime? CheckingWindowStartDate { get; init; }

    /// <summary>
    /// The start date of the window's PupilData exercise. The rules engine resolves
    /// window-relative rule dates from its year, not from the window's start date, so a
    /// data-share exercise added earlier in the year cannot move the dates the rules use (#800).
    /// Null when the window has no PupilData exercise: relative dates then cannot resolve.
    /// </summary>
    public DateTime? PupilDataExerciseStartDate { get; init; }
    public required string CheckingWindowType { get; init; }
    /// <summary>
    /// Stable machine code for the requested change: the <c>WhatToChange</c> enum
    /// name, suffixed with <c>" - {reason option value}"</c> when the flow has a
    /// <c>useAsRequestType</c> question (e.g. <c>"Remove - pupil-died"</c>). The
    /// rules engine routes on this via <c>AnswerFieldMap.WhatToChangeToOutcomeKey</c>;
    /// the display-label twin is <c>ChangeRequest.RequestType</c>.
    /// </summary>
    public required string RequestTypeCode { get; init; }
    public required SchoolDetails School { get; init; }
    public required PupilDetails Pupil { get; init; }
    public PupilDetails? MatchedPupil { get; init; }
    public required List<AnswerRecord> Answers { get; init; }

    /// <summary>
    /// The qualification and result of a results enquiry, one value in each property (#861).
    /// The enquiry ticket builder reads these values. It must not parse the challenged-subject
    /// answer text. Null for a pupil amendment.
    /// </summary>
    public EnquiryDetails? Enquiry { get; init; }
}

/// <summary>
/// The structured part of a results enquiry message. An empty string means that the journey has
/// no value for the property. The worker then leaves the Zendesk field out.
/// </summary>
public sealed class EnquiryDetails
{
    /// <summary>The enquiry type: <c>MissingQualification</c>, <c>IncorrectGrade</c> or
    /// <c>ResultDoesNotBelong</c>. It sets the Situation field on the ticket. The message holds the
    /// name, so a change to the enum order does not change it.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<WhatToChange>))]
    public required WhatToChange Type { get; init; }

    public required string Qan { get; init; }

    /// <summary>The qualification title from the qualification reference. For a result whose QAN
    /// is not in the reference, the results file's (abbreviated) qualification name.</summary>
    public required string QualificationName { get; init; }

    /// <summary>The awarding organisation, from the qualification reference. Empty for a result
    /// whose QAN is not in the reference: the results files carry no AO.</summary>
    public string AwardingOrganisation { get; init; } = string.Empty;

    /// <summary>The syllabus code that the school selected (missing qualification), or the result's
    /// syllabus code. Empty when the qualification has no syllabus codes.</summary>
    public string SyllabusCode { get; init; } = string.Empty;

    /// <summary>The grade in the results file. Empty for a missing qualification, which has no
    /// held grade.</summary>
    public string CurrentGrade { get; init; } = string.Empty;
}

public sealed class UserDetails
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>The email address of the school user who submitted the request. The enquiry
    /// ticket sends it in "Email address (CYPMD)". Empty when not supplied.</summary>
    public string Email { get; init; } = string.Empty;
}

public sealed class SchoolDetails
{
    public required string Urn { get; init; }
    public required string Name { get; init; }

    /// <summary>DfE establishment number (LAESTAB) of the requesting school. Empty when not
    /// supplied, so older messages deserialise — the DfE Establishment Number ticket field is
    /// then omitted (FR-008/FR-014).</summary>
    public string Laestab { get; init; } = string.Empty;
}

public sealed class PupilDetails
{
    public required string Id { get; init; }
    public required string CypmdId { get; init; }
    public required string Firstname { get; init; }
    public required string Surname { get; init; }
    public required string DateOfBirth { get; init; }
    public required string Sex { get; init; }
    public required int Age { get; init; }
    public required string Upn { get; init; }

    /// <summary>Inclusion status code from the pupil record. The rules engine reads
    /// <c>inclusionFlag</c> from here — it is never asked as a journey question. 0 = not supplied.</summary>
    public int Pincl { get; init; }

    /// <summary>LDS match reference (MATCHREF) for the LDS matched pupil ID ticket field.
    /// 0 = not supplied, so older messages deserialise and the field is omitted (FR-007/FR-014).</summary>
    public int MatchRef { get; init; }

    /// <summary>School admission date (ENTRYDAT) from the pupil record, the source of the
    /// Admission date ticket field (FR-012). Raw string whose format is supplier-defined;
    /// empty = not supplied, so older messages deserialise and the field is omitted (FR-014).</summary>
    public string EntryDate { get; init; } = string.Empty;
}

public sealed class AnswerRecord
{
    public required string QuestionId { get; init; }
    public required string QuestionTitle { get; init; }
    public required string Type { get; init; }

    /// <summary>The question's role, from the flow file. Null when the question has no role.
    /// Code that maps answers to ticket fields reads this, never <see cref="QuestionId"/>.</summary>
    public QuestionRole? Role { get; init; }

    /// <summary>The meaning of the chosen radio option. Null when there is no answer, or the
    /// option has no meaning.</summary>
    public OptionMeaning? Meaning { get; init; }

    /// <summary>Display value (radio option label, dd/MM/yyyy date) — used in Zendesk ticket descriptions.</summary>
    public string? Value { get; init; }

    /// <summary>
    /// Engine-facing value: the radio option's stable value, dates as yyyy-MM-dd,
    /// an Autocomplete answer's code. <c>RuleContextMapper</c> reads this in
    /// preference to <see cref="Value"/> so UI copy changes cannot affect rules.
    /// </summary>
    public string? RawValue { get; init; }

    public List<FileRecord>? Files { get; init; }
}

public sealed class FileRecord
{
    public required string OriginalFileName { get; init; }
    public required string StoredFileName { get; init; }
    public required int PageCount { get; init; }
    public required long FileSizeBytes { get; init; }
}
