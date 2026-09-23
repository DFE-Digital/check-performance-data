using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>Display names for the window dataset slots shown in the admin wizard.</summary>
/// <remarks>
/// A results dataset is named by the tag it stamps (#324), and the tag is the supplier's own file
/// name — so it is shown as well as the plain-English label. An admin matching six delivered files
/// to six upload slots needs the supplier's name to do it, and a label alone would leave them
/// guessing which of three late-results files is which.
/// </remarks>
public static class DatasetLabels
{
    /// <summary>
    /// An unmapped dataset name — e.g. an admin-typed slot name on a future custom exercise type —
    /// falls back to "Pupils" rather than throwing, matching <see cref="WindowDatasets"/>'s own
    /// "no row means no slot, not a silent misfile" stance. That fallback is deliberately the same
    /// label as the genuine <see cref="WindowDatasets.Pupils"/> case, so an unknown name is
    /// indistinguishable from a real one on screen — acceptable because nothing keys behaviour off
    /// this label, only display.
    /// </summary>
    public static string For(string datasetName) => datasetName switch
    {
        WindowDatasets.Included => "Included pupils",
        WindowDatasets.NonIncluded => "Non-included pupils",
        WindowDatasets.Pupils => "Pupils",
        ResultsFileTags.Post16Main => $"Main results ({ResultsFileTags.Post16Main})",
        ResultsFileTags.Post16LateResults1 => $"Late results 1 ({ResultsFileTags.Post16LateResults1})",
        ResultsFileTags.Post16LateResults2 => $"Late results 2 ({ResultsFileTags.Post16LateResults2})",
        ResultsFileTags.Post16Revised => $"Revised results ({ResultsFileTags.Post16Revised})",
        ResultsFileTags.Post16Retention => $"Retention ({ResultsFileTags.Post16Retention})",
        ResultsFileTags.Ks4Main => $"Main results ({ResultsFileTags.Ks4Main})",
        ResultsFileTags.Ks4LateResults1 => $"Late results 1 ({ResultsFileTags.Ks4LateResults1})",
        ResultsFileTags.Ks4LateResults2 => $"Late results 2 ({ResultsFileTags.Ks4LateResults2})",
        ResultsFileTags.Ks4Revised => $"Revised results ({ResultsFileTags.Ks4Revised})",
        WindowExercises.SummarySlot => "Summary",
        CustomExerciseDatasets.Data => "Data",
        _ => "Pupils"
    };
}
