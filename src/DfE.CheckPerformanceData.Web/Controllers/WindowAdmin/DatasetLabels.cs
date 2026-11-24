using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>Display names for the window dataset slots shown in the admin wizard.</summary>
/// <remarks>
/// A results dataset is named by the tag it stamps (#324), and the tag is the supplier's own file
/// name — so it is shown as well as the plain name. An admin matching six delivered files
/// to six upload slots needs the supplier's name to do it, and a plain name alone would leave them
/// guessing which of three late-results files is which.
/// </remarks>
public static class DatasetLabels
{
    public static string For(string datasetName) => datasetName switch
    {
        WindowDatasets.Included => "Included pupils",
        WindowDatasets.NonIncluded => "Non-included pupils",
        // A slot an admin added is shown by the name they gave it. This used to say "Pupils" for
        // every other name, so a data share's slot was labelled as pupil data.
        WindowDatasets.Pupils => "Pupils",
        // A results slot is named by its tag: shown with its plain name, from the one list of results files.
        _ when ResultsSources.NameFor(datasetName) is var name && name != datasetName => $"{name} ({datasetName})",
        _ => datasetName
    };
}
