namespace DfE.CheckPerformanceData.Application.CheckYourPupilData.Columns;

/// <summary>
/// One column of a pupils table or CSV export: its header and how to render a record's cell.
/// Rendering is a function over <see cref="IPupilRecord"/> so a column may reach the concrete
/// record for per-window-type fields.
/// </summary>
public sealed record PupilColumn(string Header, Func<IPupilRecord, string> Value);
