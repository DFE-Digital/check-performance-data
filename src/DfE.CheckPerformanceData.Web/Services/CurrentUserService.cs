using System.Security.Claims;
using DfE.CheckPerformanceData.Application.CurrentUser;

namespace DfE.CheckPerformanceData.Web.Services;

public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public string UserId =>
        httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? "system";

    public string DisplayName
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            var given = user?.FindFirst(ClaimTypes.GivenName)?.Value
                     ?? user?.FindFirst("given_name")?.Value;
            var family = user?.FindFirst(ClaimTypes.Surname)?.Value
                      ?? user?.FindFirst("family_name")?.Value;
            var name = string.Join(" ", new[] { given, family }.Where(s => s is not null));
            return name.Length > 0 ? name : "System";
        }
    }

    public string Email =>
        httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Email)?.Value
        ?? string.Empty;

    public string OrganisationId =>
        httpContextAccessor.HttpContext?.User.FindFirst("organisation_id")?.Value
        ?? "";

    public string OrganisationName =>
        httpContextAccessor.HttpContext?.User.FindFirst("organisation_name")?.Value
        ?? "";

    public string OrganisationUrn =>
        httpContextAccessor.HttpContext?.User.FindFirst("organisation_urn")?.Value
        ?? "";

    public string OrganisationTypeId =>
        httpContextAccessor.HttpContext?.User.FindFirst("organisation_type_id")?.Value
        ?? "";
}
