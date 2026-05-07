namespace DfE.CheckPerformanceData.Application.DfESignInApiClient;

public interface IDfESignInApiClient
{
    Task<OrganisationDto?> GetOrganisationAsync(string userId, string organisationId);
    Task<List<RoleDto>> GetUserRolesAsync(string orgId, string userid);
}