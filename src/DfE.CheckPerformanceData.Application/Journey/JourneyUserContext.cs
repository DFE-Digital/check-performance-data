namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// Neutral snapshot of the signed-in user, assembled in Web from
/// ICurrentUserService so the Application layer never depends on HttpContext.
/// </summary>
public sealed record JourneyUserContext
{
    public string? OrganisationUrn { get; init; }
    public string? OrganisationId { get; init; }
    public string? OrganisationName { get; init; }

    /// <summary>GIAS establishment type id (e.g. "11" = Other Independent School).</summary>
    public string? OrganisationTypeId { get; init; }
}
