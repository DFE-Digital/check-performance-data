using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>Display names for the window dataset slots shown in the admin wizard.</summary>
public static class DatasetLabels
{
    public static string For(string datasetName) => datasetName switch
    {
        WindowDatasets.Included => "Included pupils",
        WindowDatasets.NonIncluded => "Non-included pupils",
        _ => "Pupils"
    };
}
