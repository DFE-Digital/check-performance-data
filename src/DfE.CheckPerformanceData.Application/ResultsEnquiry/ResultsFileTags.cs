namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// The provenance tag stamped on each result row by ingestion, one per input CSV. Values are
/// verbatim from AB#296999 — they are a data contract with the ingestion pipeline, so a rename
/// here is a breaking change to already-written blobs.
/// </summary>
/// <remarks>
/// The 16-19 feed changes over the year. October: included, non-included and late results 1.
/// November: late results 2. February: included revised and non-included revised replace those four.
/// March: included revised with retention replaces included revised. MAIN, Revised and Retention are
/// the tags the feed used before that shape was known. They are no longer offered, but blobs already
/// written may carry them, so they stay. <see cref="ResultsSources"/> gives every tag its label.
/// </remarks>
public static class ResultsFileTags
{
    public const string Post16Included = "16to19_INC";
    public const string Post16NonIncluded = "16to19_NONINC";
    public const string Post16IncludedRevised = "16to19_INC_REV";
    public const string Post16NonIncludedRevised = "16to19_NONINC_REV";
    public const string Post16IncludedRevisedWithRetention = "16to19_INC_REV_RET";
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
