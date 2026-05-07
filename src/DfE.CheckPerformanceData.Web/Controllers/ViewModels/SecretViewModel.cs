using DfE.CheckPerformanceData.Application.DfESignInApiClient;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class SecretViewModel
{
    public string UserName { get; init; } = string.Empty;
    public required OrganisationDto Organisation { get; init; }
    public string Roles { get; init; }  = string.Empty;
}