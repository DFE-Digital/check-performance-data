using DfE.CheckPerformanceData.Application.DfESignInApiClient;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public class SecretViewModel
{
    public string UserName { get; set; }
    //public string OrganisationName { get; set; }
    public OrganisationDto Organisation { get; set; }
}