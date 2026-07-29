namespace DfE.CheckPerformanceData.Application.CheckYourPupilData.Columns;

/// <summary>
/// A rendered pupils table: headers plus already-stringified cells. The same shape feeds the
/// HTML table and the CSV export, so the two can never drift apart.
/// </summary>
public sealed record PupilTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows)
{
    public static PupilTable Empty { get; } = new([], []);

    public static PupilTable Build(IReadOnlyList<PupilColumn> columns, IReadOnlyList<IPupilRecord> pupils) =>
        new(
            columns.Select(c => c.Header).ToList(),
            pupils.Select(p => (IReadOnlyList<string>)columns.Select(c => c.Value(p)).ToList()).ToList());
}
