namespace DfE.CheckPerformanceData.Application.WindowManagement.SampleData;

/// <summary>
/// #938: a school that ingress gives sample data. <see cref="Laestab"/> is digits only (9334290)
/// and <see cref="Urn"/> is six digits.
/// </summary>
public sealed record SampleSchool(string Laestab, string Urn);
