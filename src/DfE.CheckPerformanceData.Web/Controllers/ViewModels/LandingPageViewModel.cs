namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public class LandingPageViewModel(
    IEnumerable<LandingPageWindowViewModel> windows,
    string? organisationName,
    string? organisationUrn,
    string? organisationLaestab,
    string? keyStages,
    string address)
{
    public IEnumerable<LandingPageWindowViewModel> Windows { get; } = windows;
    public string? OrganisationName { get; } = organisationName;
    public string? OrganisationUrn { get; } = organisationUrn;
    public string? OrganisationLaestab { get; } = organisationLaestab;
    public string? KeyStages { get; } = keyStages;
    public string OrganisationAddress { get; } = address;
}

public class LandingPageWindowViewModel
{
    public required string Title { get; init; }
    public required DateTime EndDate { get; init; }
    public required Guid Id { get; init; }
}