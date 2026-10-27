namespace DfE.CheckPerformanceData.Web.Extensions;

/// <summary>
/// Marks a response that carries personal data as requiring <c>Cache-Control: no-store</c>, written
/// through <see cref="HttpResponseExtensions.NoStore"/> (OWASP A04, #615).
///
/// The guard in NoStoreDownloadSourceGuardTests reflects over this marker and over every
/// <c>File(...)</c> response in the assembly, so a new personal-data download cannot ship
/// unclassified: it either carries this marker or is listed as deliberately excluded, with a reason.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresNoStoreAttribute : Attribute;
