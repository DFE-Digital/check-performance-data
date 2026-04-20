using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public class LandingPageViewModel(
    IEnumerable<LandingPageWindowViewModel> windows,
    string? organisationName,
    string? organisationUrn,
    string? organisationLaestab,
    string? keyStages)
{
    public IEnumerable<LandingPageWindowViewModel> Windows { get; } = windows;
    public string? OrganisationName { get; } = organisationName;
    public string? OrganisationUrn { get; } = organisationUrn;
    public string? OrganisationLaestab { get; } = organisationLaestab;
    public string? KeyStages { get; } = keyStages;
}

public class LandingPageWindowViewModel
{
    public required string Title { get; init; }
    public required DateOnly EndDate { get; init; }
}