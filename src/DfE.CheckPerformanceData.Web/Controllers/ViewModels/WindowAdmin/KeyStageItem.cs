using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

public sealed class KeyStageItem : AdminPage
{
    public IEnumerable<KeyStages> KeyStages { get; set; } = [];
    public KeyStages? KeyStage { get; set; }    
}