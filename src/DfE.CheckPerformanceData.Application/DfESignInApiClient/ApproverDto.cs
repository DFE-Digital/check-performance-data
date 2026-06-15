namespace DfE.CheckPerformanceData.Application.DfESignInApiClient;

public sealed class ApproverDto
{
    public required string UserId { get; init; }
    public required string GivenName { get; init; }
    public required string FamilyName { get; init; }
    public required string Email { get; init; }
    public int RoleId { get; init; }
    public string RoleName { get; init; } = string.Empty;
    public int UserStatus { get; init; }
    public required ApproverOrganisationDto Organisation { get; init; }
}

public sealed class ApproverOrganisationDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Urn { get; init; }
    public string? EstablishmentNumber { get; init; }
}

public sealed class ApproversResponseDto
{
    public List<ApproverDto> Users { get; init; } = [];
    public int NumberOfRecords { get; init; }
    public int Page { get; init; }
    public int NumberOfPages { get; init; }
}
