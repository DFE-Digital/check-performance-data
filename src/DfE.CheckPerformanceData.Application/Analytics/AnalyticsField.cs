namespace DfE.CheckPerformanceData.Application.Analytics;

/// <summary>
/// A single key/value pair on an <see cref="AnalyticsEvent"/>. <see cref="Hidden"/>
/// marks data the Infrastructure adapter must send via the policy-tagged, masked
/// "hidden data" channel (BigQuery <c>hidden_data</c>) rather than as plain data —
/// the PII boundary the analytics design mandates.
/// </summary>
public sealed record AnalyticsField(string Name, object? Value, bool Hidden = false);
