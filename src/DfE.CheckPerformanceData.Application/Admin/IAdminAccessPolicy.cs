using System.Security.Claims;

namespace DfE.CheckPerformanceData.Application.Admin;

// Reads the role-section-access grid and answers "is this user allowed in this section?"
// Section keys are AdminNavKeys values (or any string a controller/attribute wants to gate on).
public interface IAdminAccessPolicy
{
    /// <summary>
    /// True when at least one of the user's roles has an access grant for <paramref name="sectionKey"/>.
    /// </summary>
    Task<bool> CanAccessAsync(ClaimsPrincipal user, string sectionKey);

    /// <summary>Every stored grant, for the settings UI.</summary>
    Task<IReadOnlyList<RoleSectionAccessGrant>> GetAllGrantsAsync();

    /// <summary>
    /// Atomically overwrites the grants for <paramref name="roleName"/> with
    /// <paramref name="allowedSections"/>. Removes the row entirely for empty lists.
    /// </summary>
    Task SetGrantsForRoleAsync(string roleName, IReadOnlyList<string> allowedSections, string? userId);
}
