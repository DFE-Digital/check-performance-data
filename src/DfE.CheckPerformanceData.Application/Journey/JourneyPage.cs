namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class JourneyPage
{
    public const string PrimaryKey = "primary";
    public const string MatchKey = "match";

    public required string Id { get; init; }
    public PageType Type { get; init; } = PageType.Question;
    public string? Title { get; init; }

    /// <summary>
    /// Sanitised title for the browser &lt;title&gt; element (and therefore analytics).
    /// Set this when <see cref="Title"/> embeds the pupil name so the name does not
    /// leak into the page title. When unset the browser title falls back to a
    /// pupil-name-free version of <see cref="Title"/> (see <see cref="JourneyTemplate.Strip"/>).
    /// </summary>
    public string? PageTitle { get; init; }

    public string? Subheading { get; init; }
    public string? Content { get; init; }
    public bool RequireAtLeastOne { get; init; }
    public List<Question> Questions { get; init; } = [];
    public string? NextPageId { get; init; }
    public PupilFilter? PupilFilter { get; init; }
    public string? PupilKey { get; init; }

    /// <summary>
    /// PupilSearch pages only: limit the search to students the school holds a result for. Set on
    /// a results enquiry, where a student with no result has no grade to correct. Independent of
    /// <see cref="PupilFilter"/>, which selects the population by inclusion status.
    /// </summary>
    public bool RequireResults { get; init; }
    public string? ValidationFailure { get; init; }

    /// <summary>
    /// AB#297310: this page's answers ARE the pupil. On a successful POST the journey engine
    /// mints a synthetic <c>PupilRecord</c> from them (see <c>AddPupilJourney.BuildPupil</c>)
    /// and stores it as <c>SelectedPupil</c>, standing in for the pupil-search step the Add
    /// journey does not have.
    /// </summary>
    public bool PupilFromAnswers { get; init; }
}
