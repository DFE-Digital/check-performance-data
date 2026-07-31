namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

/// <summary>
/// One CSV + schema pair to be ingested in a run. A KS4 window has exactly one
/// (<c>Included = null</c>); a Post16 window has two, because LDS supplies included and
/// non-included pupils as separate files and the non-included file has no <c>P_INCL</c> column.
/// </summary>
/// <param name="Name">Dataset name for progress messages, e.g. "included" / "nonincluded".</param>
/// <param name="Included">
/// <c>true</c>/<c>false</c> stamps an <c>INCLUDED</c> marker on every record from this file, so
/// inclusion is decided by file of origin. <c>null</c> means the record carries its own
/// inclusion signal (KS4's <c>P_INCL</c>) and nothing is stamped.
/// </param>
public sealed record IngressDataset(
    string Name,
    string InputCsvFile,
    string InputCsvChecksum,
    string SchemaFile,
    string SchemaChecksum,
    bool? Included);
