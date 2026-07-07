using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;

namespace DfE.CheckPerformanceData.Application.Admin;

public sealed class AdminAccessPolicy(
    IAdminSectionAccessRepository repository,
    IMemoryCache cache) : IAdminAccessPolicy
{
    // Grants change rarely (a human edits them in the settings UI). Cache the full set for a
    // minute so per-request access checks don't hit the database. The cache is invalidated
    // whenever a grant is saved.
    private const string CacheKey = "AdminAccessPolicy.Grants";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(1);

    public async Task<bool> CanAccessAsync(ClaimsPrincipal user, string sectionKey)
    {
        if (string.IsNullOrEmpty(sectionKey)) return false;
        var grants = await LoadGrantsAsync();
        var roleSections = grants
            .Where(g => g.SectionKey.Equals(sectionKey, StringComparison.OrdinalIgnoreCase))
            .Select(g => g.RoleName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return roleSections.Any(user.IsInRole);
    }

    public Task<IReadOnlyList<RoleSectionAccessGrant>> GetAllGrantsAsync() => LoadGrantsAsync();

    public async Task SetGrantsForRoleAsync(string roleName, IReadOnlyList<string> allowedSections, string? userId)
    {
        var cleaned = allowedSections
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        await repository.ReplaceRoleAsync(roleName.Trim(), cleaned, userId);
        cache.Remove(CacheKey);
    }

    private Task<IReadOnlyList<RoleSectionAccessGrant>> LoadGrantsAsync()
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<RoleSectionAccessGrant>? cached) && cached is not null)
            return Task.FromResult(cached);

        return LoadAndCacheAsync();

        async Task<IReadOnlyList<RoleSectionAccessGrant>> LoadAndCacheAsync()
        {
            var loaded = await repository.GetAllAsync();
            cache.Set(CacheKey, loaded, CacheTtl);
            return loaded;
        }
    }
}
