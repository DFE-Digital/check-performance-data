namespace DfE.CheckPerformanceData.Domain.Enums;

/// <summary>
/// The LDS output files the egress can produce (AB#294553). Merged learners is deliberately absent —
/// it needs its own column set and the KS4 June 20→21 correction-code rule (AB#292610) and is a
/// follow-up. Stored as a string, so the order here is display order, not identity.
/// </summary>
public enum EgressOutputType
{
    NewLearners,
    RemoveLearners
}
