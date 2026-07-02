namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public class ConfirmedCorrectViewModel(string endDate, string referenceNumber)
{
    public string EndDate { get; } = endDate;
    public string ReferenceNumber { get; } = referenceNumber;
}