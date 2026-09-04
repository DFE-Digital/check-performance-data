using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class LandingPageViewModel(
    IEnumerable<LandingPageWindowViewModel> openWindows,
    string? organisationName,
    string? organisationUrn,
    string? organisationLaestab,
    string? keyStages,
    string address,
    IReadOnlyList<LandingPageBannerViewModel> noDataWindows,
    IReadOnlyList<LandingPageBannerViewModel> notValidWindows,
    IReadOnlyList<LandingPageClosedWindowViewModel> closedWindows)
{
    public IEnumerable<LandingPageWindowViewModel> OpenWindows { get; } = openWindows;
    public string? OrganisationName { get; } = organisationName;
    public string? OrganisationUrn { get; } = organisationUrn;
    public string? OrganisationLaestab { get; } = organisationLaestab;
    public string? KeyStages { get; } = keyStages;
    public string OrganisationAddress { get; } = address;
    /// <summary>
    /// One banner per window rather than one sentence naming several: the "no data" banner names a
    /// learner, and a school with both a KS4 and a 16-19 window has no single word for one.
    /// </summary>
    public IReadOnlyList<LandingPageBannerViewModel> NoDataWindows { get; } = noDataWindows;

    public IReadOnlyList<LandingPageBannerViewModel> NotValidWindows { get; } = notValidWindows;

    /// <summary>
    /// AB#298317: one banner per card window whose pupil-data exercise has closed. Per window,
    /// like the other two, because each carries its own next-opportunity date and learner noun.
    /// </summary>
    public IReadOnlyList<LandingPageClosedWindowViewModel> ClosedWindows { get; } = closedWindows;
}

/// <summary>One window named in a landing-page notification banner, with its own learner noun.</summary>
public sealed class LandingPageBannerViewModel
{
    public required string Title { get; init; }
    public required LearnerNoun LearnerNoun { get; init; }
}

/// <summary>
/// AB#298317: the "data checking window has closed" banner for one window. Pupil data checking has
/// shut; results enquiry may or may not still be running.
/// </summary>
public sealed class LandingPageClosedWindowViewModel
{
    public required string Title { get; init; }
    /// <summary>Already formatted as month + year; null omits the sentence.</summary>
    public string? NextOpportunity { get; init; }
    public bool IsResultsEnquiryOpen { get; init; }
    public required LearnerNoun LearnerNoun { get; init; }
}

/// <summary>
/// One landing card. Every date here is an exercise's own, never the outer window's — on a 16-19
/// window the outer end is the results-enquiry close, months after pupil data shuts (AB#298317).
/// Dates are pre-formatted in the controller the way the card has always printed them.
/// </summary>
public sealed class LandingPageWindowViewModel
{
    public required string Title { get; init; }
    public required Guid Id { get; init; }
    public bool HasPupilData { get; init; }
    public required LearnerNoun LearnerNoun { get; init; }

    public bool IsPupilDataOpen { get; init; }
    /// <summary>"5pm" — for the open-state "You have until … on …" sentence.</summary>
    public string? PupilDataEndTime { get; init; }
    /// <summary>"Friday 30 October 2026" — for the open-state sentence.</summary>
    public string? PupilDataEndDate { get; init; }
    /// <summary>"5 October" — for the closed-state amendment range.</summary>
    public string? PupilDataRangeStart { get; init; }
    /// <summary>"16 October 2026" — for the closed-state amendment range.</summary>
    public string? PupilDataRangeEnd { get; init; }

    public bool IsResultsEnquiryOpen { get; init; }
    /// <summary>"31 March 2027" — null when the window runs no results enquiry.</summary>
    public string? ResultsEnquiryEndDate { get; init; }
}
