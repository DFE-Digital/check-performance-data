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
    IReadOnlyList<LandingPageBannerViewModel> notValidWindows)
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
}

/// <summary>One window named in a landing-page notification banner, with its own learner noun.</summary>
public sealed class LandingPageBannerViewModel
{
    public required string Title { get; init; }
    public required LearnerNoun LearnerNoun { get; init; }
}

public sealed class LandingPageWindowViewModel
{
    public required string Title { get; init; }
    public required string EndDate { get; init; }
    public required string EndTime { get; init; }
    public required Guid Id { get; init; }
    public bool HasPupilData { get; init; }
    public required string StartDate { get; init; }
}