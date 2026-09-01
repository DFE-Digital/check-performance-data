using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

public sealed class PupilSearchViewModel
{
    public Guid WindowId { get; set; }

    /// <summary>
    /// The window's word for a learner, from <c>RequestState.LearnerNoun</c>. Only the fallback
    /// wording needs it — a page that sets its own title, hint or validationFailure in the flow
    /// config already spells the noun itself, because a config is per window type.
    /// </summary>
    public LearnerNoun LearnerNoun { get; set; } = LearnerNoun.Pupil;
    public string PageId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public PupilFilter Filter { get; set; }
    public Guid? ExcludePupilId { get; set; }

    /// <summary>Ask the suggestions endpoint for students who hold results only. See
    /// <see cref="Application.Journey.JourneyPage.RequireResults"/>.</summary>
    public bool RequireResults { get; set; }
    public string? SelectedPupilId { get; set; }
    public string? SelectedPupilLabel { get; set; }
    public string? Hint { get; set; }
    public string? BackPageId { get; set; }
    public bool BackPageIsPupilSearch { get; set; }

    /// <summary>The JourneyController action that serves <see cref="BackPageId"/>.</summary>
    public string BackPageAction { get; set; } = nameof(JourneyController.Page);
    public string? ConflictErrorReference { get; set; }
    public string? ConflictErrorLink { get; set; }
    public string? ConflictPupilName { get; set; }
    public string? ConflictReasonType { get; set; }
    public string? ConflictUserName { get; set; }
    public string? ConflictAttentionHtml { get; set; }
}
