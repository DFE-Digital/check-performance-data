namespace DfE.CheckPerformanceData.Application.CheckYourPupilData.Results;

/// <summary>One column of the results table or its CSV export: header plus cell renderer.</summary>
public sealed record ResultColumn(string Header, Func<ResultRow, string> Value);
