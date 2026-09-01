using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

/// <summary>
/// AB#297310: the Add journey's learner-details contract. The Add_*.json flows must use
/// these page and question ids — AddFlowTests pins them, exactly as the AB#296648 enquiry
/// constants are pinned. BuildPupil turns the page's answers into the synthetic pupil the
/// rest of the journey machinery keys on (<see cref="RequestState.SelectedPupil"/>, typed
/// <see cref="PupilDto"/> — NOT the supplier-file <see cref="PupilRecord"/>, which has no
/// bearing on a pupil that was never in a supplier file).
/// </summary>
public static class AddPupilJourney
{
    public const string LearnerDetailsPageId = "learner-details";
    public const string AdmissionDetailsPageId = "admission-details";
    public const string FirstNameQuestionId = "first-name";
    public const string LastNameQuestionId = "last-name";
    public const string DateOfBirthQuestionId = "date-of-birth";
    public const string SexQuestionId = "sex";
    public const string UpnQuestionId = "upn";

    /// <summary>
    /// The route/page identity of the AB#297780 duplicate-check warning page. Not part of the
    /// Add_*.json question flows — it is intercepted after the learner-details post and mirrors
    /// the learner-details redirect target when there are no matches.
    /// </summary>
    public const string DuplicateCheckPageId = "duplicate-check";

    /// <summary>Query/route key carrying the selected pupil id for an Include / Switch-to-Include hand-off.</summary>
    public const string DuplicateCheckPupilIdKey = "pupilId";

    /// <summary>
    /// The window types an Add_*.json exists for. Single source of truth for the Add radio on
    /// What to change and for the guard on the post it produces — a window missing from here
    /// must not open the journey even if a flow file is later uploaded for it.
    /// </summary>
    public static readonly IReadOnlySet<CheckingWindowType> SupportedWindowTypes =
        new HashSet<CheckingWindowType>
        {
            CheckingWindowType.KS4June,
            CheckingWindowType.KS4Autumn,
            CheckingWindowType.KS2
        };

    public static PupilDto BuildPupil(RequestState journey, Guid? existingId)
    {
        string Text(string questionId) =>
            journey.QuestionAnswers.TryGetValue(questionId, out var a) ? a.TextValue?.Trim() ?? "" : "";

        // dd/MM/yyyy matches the supplier-file format the rest of the pipeline already
        // parses and displays (BuildAnswerRecord, Zendesk composition).
        string Dob() =>
            journey.QuestionAnswers.TryGetValue(DateOfBirthQuestionId, out var a) && a.DateValue is { } d
                ? $"{d.Day:D2}/{d.Month:D2}/{d.Year:D4}"
                : "";

        return new PupilDto
        {
            // Stable across summary edits so drafts upsert one row; fresh per journey so
            // the one-request-per-pupil rule can never collide two different typed-in
            // pupils (dataset matching is deliberately out of scope — AB#297780).
            Id = existingId ?? Guid.NewGuid(),
            Firstname = Text(FirstNameQuestionId),
            Surname = Text(LastNameQuestionId),
            Sex = Text(SexQuestionId),
            DateOfBirth = Dob(),
            Identifier = Text(UpnQuestionId),
            // Age/Cypmd_Id/Pincl/MatchRef/Laestab/EntryDate have no equivalent on the
            // learner-details page and are never read for an Add submission — Age/Cypmd_Id
            // only feed BuildRequestDocument and the result-suggestions lookup, both of
            // which an Add journey never reaches (no rules-engine enqueue, no result search).
            Age = 0,
            Cypmd_Id = string.Empty
        };
    }
}
