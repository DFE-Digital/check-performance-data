namespace DfE.CheckPerformanceData.Application.Search;

// Gates whether the <!-- rank: N --> HTML comment renders on /search. The comment
// exposes the internal ts_rank score alongside each hit — useful for tuning weights
// during development, undesirable in Production where it leaks the internal ranking
// scheme. Default: true in Development, false in Production. Ops can flip in
// Production without a rebuild via the Search:ShowDebug configuration key.
public interface ISearchDebugOptions
{
    bool ShowSearchDebug { get; }
}
