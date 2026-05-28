using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class SettingRepository(IPortalDbContext context) : ISettingRepository
{
    public async Task<Dictionary<string, string>> GetAllAsync() =>
        await context.Settings
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Key, s => s.Value);

    public async Task<string?> GetValueAsync(string key) =>
        (await context.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key))?.Value;

    public async Task UpsertAsync(string key, string value)
    {
        var existing = await context.Settings.FirstOrDefaultAsync(s => s.Key == key);
        if (existing is null)
            context.Settings.Add(new Setting { Key = key, Value = value });
        else
            existing.Value = value;

        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(string key)
    {
        var existing = await context.Settings.FirstOrDefaultAsync(s => s.Key == key);
        if (existing is null)
            return;

        context.Settings.Remove(existing);
        await context.SaveChangesAsync();
    }
}
