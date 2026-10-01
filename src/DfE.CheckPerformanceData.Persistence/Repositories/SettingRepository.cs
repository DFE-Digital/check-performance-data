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

    public Task ExecuteInTransactionAsync(Func<Task> work) =>
        context.ExecuteInTransactionAsync(async () =>
        {
            // The context keeps the entities it saved inside the transaction. After a rollback, or
            // before the retry strategy runs this again, they would hold values the database does
            // not, and a later upsert of the same value would be skipped as unchanged. Forget them.
            context.ChangeTracker.Clear();
            try
            {
                await work();
            }
            catch
            {
                context.ChangeTracker.Clear();
                throw;
            }
        });

    public async Task DeleteAsync(string key)
    {
        var existing = await context.Settings.FirstOrDefaultAsync(s => s.Key == key);
        if (existing is null)
            return;

        context.Settings.Remove(existing);
        await context.SaveChangesAsync();
    }
}
