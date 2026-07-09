using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

public class WindowTypeItem : AdminPage
{
    public IEnumerable<CheckingWindowType> Types { get; set; } = [];
    public CheckingWindowType? WindowType { get; set; }    
}