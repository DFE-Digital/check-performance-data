using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>One results file of the supplier feed: the tag stamped on its rows and its name for schools.</summary>
/// <param name="IsSecondLateResults">
/// The file that corrects nearly all incorrect grades. While its slot is in use and the live release
/// has not read it, the enquiry journey tells a school to wait for it (<see cref="LateResultsAvailability"/>).
/// </param>
/// <param name="IsRequired">
/// A file the exercise starts with: its slot must hold a file before the exercise can be validated.
/// The later files land weeks apart and one may never land, so they are optional.
/// </param>
public sealed record ResultsSource(string Tag, string Label, bool IsSecondLateResults = false, bool IsRequired = false);

/// <summary>
/// The results files each window type offers, in the order they arrive, and the label schools see
/// for each tag. This is the only list of results files: the default slots, the admin's source
/// dropdown, the late-results check and every label read it. A new supplier file is one row here.
/// </summary>
public static class ResultsSources
{
    private static readonly IReadOnlyList<ResultsSource> Post16 =
    [
        // A 16-19 results enquiry starts with these three files.
        new(ResultsFileTags.Post16Included, "Included", IsRequired: true),
        new(ResultsFileTags.Post16NonIncluded, "Non-included", IsRequired: true),
        new(ResultsFileTags.Post16LateResults1, "Late results 1", IsRequired: true),
        new(ResultsFileTags.Post16LateResults2, "Late results 2", IsSecondLateResults: true),
        new(ResultsFileTags.Post16IncludedRevised, "Included revised"),
        new(ResultsFileTags.Post16NonIncludedRevised, "Non-included revised"),
        new(ResultsFileTags.Post16IncludedRevisedWithRetention, "Included revised with retention")
    ];

    private static readonly IReadOnlyList<ResultsSource> Ks4 =
    [
        new(ResultsFileTags.Ks4Main, "Main results", IsRequired: true),
        new(ResultsFileTags.Ks4LateResults1, "Late results 1"),
        new(ResultsFileTags.Ks4LateResults2, "Late results 2", IsSecondLateResults: true),
        new(ResultsFileTags.Ks4Revised, "Revised results")
    ];

    // No longer offered, but blobs and releases written before may carry them.
    private static readonly IReadOnlyList<ResultsSource> Retired =
    [
        new(ResultsFileTags.Post16Main, "Main results"),
        new(ResultsFileTags.Post16Revised, "Revised results"),
        new(ResultsFileTags.Post16Retention, "Retention")
    ];

    private static readonly IReadOnlyDictionary<string, ResultsSource> ByTag =
        Post16.Concat(Ks4).Concat(Retired).ToDictionary(s => s.Tag, StringComparer.Ordinal);

    /// <summary>The files a window type offers, in the order they arrive. Empty for KS2, which has no results feed.</summary>
    public static IReadOnlyList<ResultsSource> For(CheckingWindowType type) => type switch
    {
        CheckingWindowType.Post16 => Post16,
        CheckingWindowType.KS4June or CheckingWindowType.KS4Autumn => Ks4,
        _ => []
    };

    /// <summary>The label for a tag. An unknown tag is shown as it is, rather than hidden.</summary>
    public static string LabelFor(string? tag) =>
        tag is not null && ByTag.TryGetValue(tag, out var source) ? source.Label : tag ?? string.Empty;

    public static bool IsSecondLateResults(string? tag) =>
        tag is not null && ByTag.TryGetValue(tag, out var source) && source.IsSecondLateResults;
}
