namespace DfE.CheckPerformanceData.Application.Admin;

// A single cell in the role-vs-section grid — presence of (RoleName, SectionKey) grants access.
public sealed record RoleSectionAccessGrant(string RoleName, string SectionKey);
