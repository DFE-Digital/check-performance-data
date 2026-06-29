namespace DfE.CheckPerformanceData.Application.ContentStaging;

// How an import treats content that already exists in the target environment.
public enum ContentImportMode
{
    // Leave existing wiki pages / content blocks untouched; only add what is missing.
    Skip,

    // Overwrite existing pages / blocks with the bundle's content (recording a new version).
    Replace,

    // Abort the whole import if the bundle would touch anything that already exists.
    Fail
}
