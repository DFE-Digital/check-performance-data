namespace DfE.CheckPerformanceData.Application.Search;

// Compile-only stub — the real Interlocked-backed implementation lands in a follow-on
// commit. This shape exists purely so the whole solution compiles while the counter
// unit tests run RED against NotImplementedException rather than against a missing type
// (which would surface as a compile error and mask the failing-test signal). Delete this
// stub the moment the real implementation lands.
public sealed class SearchZeroResultsCounter : ISearchZeroResultsCounter
{
    public void Increment() => throw new NotImplementedException();

    public long Read() => throw new NotImplementedException();
}
