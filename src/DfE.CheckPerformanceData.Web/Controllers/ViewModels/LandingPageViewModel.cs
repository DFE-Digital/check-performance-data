namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public class LandingPageViewModel(
    IEnumerable<LandingPageWindowViewModel> openWindows,
    string? organisationName,
    string? organisationUrn,
    string? organisationLaestab,
    string? keyStages,
    string address,
    IEnumerable<LandingPageWindowViewModel> closedWindows)
{
    public IEnumerable<LandingPageWindowViewModel> OpenWindows { get; } = openWindows;
    public IEnumerable<LandingPageWindowViewModel> ClosedWindows { get; } = closedWindows;
    public string? OrganisationName { get; } = organisationName;
    public string? OrganisationUrn { get; } = organisationUrn;
    public string? OrganisationLaestab { get; } = organisationLaestab;
    public string? KeyStages { get; } = keyStages;
    public string OrganisationAddress { get; } = address;
}

public class LandingPageWindowViewModel
{
    public required string Title { get; init; }
    public required string EndDate { get; init; }
    public required string EndTime { get; init; }
    public required Guid Id { get; init; }
    public bool HasPupilData { get; init; }
    public required string StartDate { get; init; }
}