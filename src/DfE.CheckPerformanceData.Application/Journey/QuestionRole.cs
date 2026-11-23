using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// What a question means. A question declares its role in the flow file (<c>"role"</c>), and
/// code finds an answer by role, never by question id. See docs/journey-config-first.md,
/// pattern 4. A role occurs at most once in a flow.
/// </summary>
/// <remarks>
/// The JSON name is the dotted name, in the flow file and in the queue message. Add a role
/// only when code reads it.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<QuestionRole>))]
public enum QuestionRole
{
    /// <summary>Does the enquiry affect the whole cohort? The options carry the meanings
    /// <see cref="OptionMeaning.Yes"/> and <see cref="OptionMeaning.No"/>.</summary>
    [JsonStringEnumMemberName("Enquiry.CohortScope")]
    EnquiryCohortScope,

    /// <summary>The number of missing or incorrect qualifications in the cohort.</summary>
    [JsonStringEnumMemberName("Enquiry.CohortCount")]
    EnquiryCohortCount,

    /// <summary>The award date of a missing qualification.</summary>
    [JsonStringEnumMemberName("Enquiry.AwardDate")]
    EnquiryAwardDate,

    /// <summary>The grade that the school gives: the missing grade or the revised grade.</summary>
    [JsonStringEnumMemberName("Enquiry.Grade")]
    EnquiryGrade,

    /// <summary>The National Centre Number (NCN) of the exam centre.</summary>
    [JsonStringEnumMemberName("Enquiry.Ncn")]
    EnquiryNcn,

    /// <summary>The syllabus code that the school selects for a missing qualification.</summary>
    [JsonStringEnumMemberName("Enquiry.SyllabusCode")]
    EnquirySyllabusCode,

    /// <summary>The additional comments of an enquiry.</summary>
    [JsonStringEnumMemberName("Enquiry.AdditionalComments")]
    EnquiryAdditionalComments
}
