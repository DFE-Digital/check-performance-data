namespace DfE.CheckPerformanceData.Application.Admin;

public interface IAdminSectionAccessRepository
{
    /// <summary>Every current (role, section) grant. Absence of a pair means denied.</summary>
    Task<IReadOnlyList<RoleSectionAccessGrant>> GetAllAsync();

    /// <summary>
    /// Atomically replaces the grants for <paramref name="roleName"/>: deletes any existing
    /// rows for that role and inserts one row per <paramref name="allowedSections"/>.
    /// </summary>
    Task ReplaceRoleAsync(string roleName, IReadOnlyList<string> allowedSections, string? userId);

    /// <summary>Bulk-insert grants that do not yet exist. Used by the default seeder on empty databases.</summary>
    Task SeedAsync(IReadOnlyList<RoleSectionAccessGrant> grants, string? userId);

    Task<bool> HasAnyAsync();
}
