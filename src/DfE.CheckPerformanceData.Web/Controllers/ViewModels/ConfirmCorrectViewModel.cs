namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public class ConfirmCorrectViewModel(Guid windowId, string endDate)
{
    public Guid WindowId { get; } = windowId;
    public string EndDate { get; } = endDate;
}