using DfE.CheckPerformanceData.Application.Admin;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class AdminSectionAccessRepository(IPortalDbContext context) : IAdminSectionAccessRepository
{
    public async Task<IReadOnlyList<RoleSectionAccessGrant>> GetAllAsync()
    {
        var rows = await context.AdminSectionAccesses
            .AsNoTracking()
            .Select(a => new { a.RoleName, a.SectionKey })
            .ToListAsync();
        return rows.Select(r => new RoleSectionAccessGrant(r.RoleName, r.SectionKey)).ToList();
    }

    public async Task ReplaceRoleAsync(string roleName, IReadOnlyList<string> allowedSections, string? userId)
    {
        await context.ExecuteInTransactionAsync(async () =>
        {
            var existing = await context.AdminSectionAccesses
                .Where(a => a.RoleName == roleName)
                .ToListAsync();
            context.AdminSectionAccesses.RemoveRange(existing);

            var now = DateTime.UtcNow;
            foreach (var section in allowedSections)
            {
                await context.AdminSectionAccesses.AddAsync(new AdminSectionAccess
                {
                    RoleName = roleName,
                    SectionKey = section,
                    CreatedAt = now,
                    CreatedBy = userId,
                });
            }
            await context.SaveChangesAsync();
        });
    }

    public async Task SeedAsync(IReadOnlyList<RoleSectionAccessGrant> grants, string? userId)
    {
        if (grants.Count == 0) return;
        var now = DateTime.UtcNow;
        foreach (var g in grants)
        {
            var exists = await context.AdminSectionAccesses
                .AnyAsync(a => a.RoleName == g.RoleName && a.SectionKey == g.SectionKey);
            if (exists) continue;

            await context.AdminSectionAccesses.AddAsync(new AdminSectionAccess
            {
                RoleName = g.RoleName,
                SectionKey = g.SectionKey,
                CreatedAt = now,
                CreatedBy = userId,
            });
        }
        await context.SaveChangesAsync();
    }

    public Task<bool> HasAnyAsync() => context.AdminSectionAccesses.AnyAsync();
}
