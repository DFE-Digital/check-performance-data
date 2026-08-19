namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// The provenance tag stamped on each result row by ingestion, one per input CSV. Values are
/// verbatim from AB#296999 — they are a data contract with the ingestion pipeline, so a rename
/// here is a breaking change to already-written blobs.
/// </summary>
public static class ResultsFileTags
{
    public const string Post16Main = "16to19_MAIN";
    public const string Post16LateResults1 = "16to19_LR1";
    public const string Post16LateResults2 = "16to19_LR2";
    public const string Post16Revised = "16to19_Revised";
    public const string Post16Retention = "16to19_Retention";
    public const string Ks4Main = "KS4_MAIN";
    public const string Ks4LateResults1 = "KS4_LR1";
    public const string Ks4LateResults2 = "KS4_LR2";
    public const string Ks4Revised = "KS4_Revised";
}
