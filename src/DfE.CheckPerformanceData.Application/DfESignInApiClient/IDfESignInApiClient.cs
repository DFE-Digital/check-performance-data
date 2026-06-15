namespace DfE.CheckPerformanceData.Application.DfESignInApiClient;

public interface IDfESignInApiClient
{
    Task<OrganisationDto?> GetOrganisationAsync(string userId, string organisationId);
    Task<List<RoleDto>> GetUserRolesAsync(string orgId, string userid);
     Task<ApproversResponseDto?> GetApproversAsync(int page = 1, int pageSize = 25);
    Task<ApproversResponseDto?> GetOrganisationApproversAsync(string organisationId, int page = 1, int pageSize = 25);
    Task<OrganisationUsersResponseDto?> GetOrganisationUsersAsync(string ukprn, string[]? roles = null);
}