namespace DfE.CheckPerformanceData.Application.Analytics;

// One row per hit returned by a search request. ResultKind is either "page" or "block";
// ResultKey is the canonical URL for a page or the block key for a block. Positional
// record mirrors the persistable shape so a sink writer can Add(new SearchEventResult
// { ... }) without a mapper class in between.
public sealed record SearchEventResultDto(
    int Position,
    string ResultKind,
    string ResultKey,
    float Rank);
