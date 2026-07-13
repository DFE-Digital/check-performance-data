using DfE.CheckPerformanceData.Infrastructure.Ingress;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

public class ValidationViewModel : AdminPage
{
    public ProcessingResult? ProcessingResult { get; set; }   
}