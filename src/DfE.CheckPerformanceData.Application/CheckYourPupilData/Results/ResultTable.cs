using DfE.CheckPerformanceData.Application.CheckYourPupilData.Columns;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData.Results;

/// <summary>
/// Projects result rows into the same headers-plus-cells shape the pupil sections use, so the
/// table partial and the CSV generator need no results-specific variant.
/// </summary>
public static class ResultTable
{
    public static PupilTable Build(IReadOnlyList<ResultColumn> columns, IReadOnlyList<ResultRow> rows) =>
        new(
            columns.Select(c => c.Header).ToList(),
            rows.Select(r => (IReadOnlyList<string>)columns.Select(c => c.Value(r)).ToList()).ToList());
}
