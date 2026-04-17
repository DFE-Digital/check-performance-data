using System.Text.Json;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Persistence.Configurations;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DfE.CheckPerformanceData.Persistence.Contexts;

public sealed class PortalDbContext(
    DbContextOptions<PortalDbContext> options,
    ICurrentUserService currentUserService) : DbContext(options)
{
    public DbSet<CheckingWindow> CheckingWindows => Set<CheckingWindow>();
    public DbSet<ContentBlock> ContentBlocks => Set<ContentBlock>();
    public DbSet<ContentBlockVersion> ContentBlockVersions => Set<ContentBlockVersion>();
    public DbSet<WikiPage> WikiPages => Set<WikiPage>();
    public DbSet<WikiPageVersion> WikiPageVersions => Set<WikiPageVersion>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ContentBlockConfiguration());
        modelBuilder.ApplyConfiguration(new ContentBlockVersionConfiguration());
        modelBuilder.ApplyConfiguration(new WikiPageConfiguration());
        modelBuilder.ApplyConfiguration(new WikiPageVersionConfiguration());
        modelBuilder.ApplyConfiguration(new AuditEntryConfiguration());
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var pendingAudits = CollectAuditEntries();
        var result = await base.SaveChangesAsync(cancellationToken);

        if (pendingAudits.Count > 0)
        {
            await SavePendingAuditEntries(pendingAudits, cancellationToken);
        }

        return result;
    }

    public async Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default)
    {
        var strategy = Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
            await work();
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private List<(AuditEntry Audit, EntityEntry Entry, bool HasTempKey)> CollectAuditEntries()
    {
        ChangeTracker.DetectChanges();
        var entries = new List<(AuditEntry, EntityEntry, bool)>();

        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is AuditEntry) continue;
            if (entry.State is EntityState.Detached or EntityState.Unchanged) continue;

            var audit = new AuditEntry
            {
                EntityType = entry.Entity.GetType().Name,
                Timestamp = DateTime.UtcNow,
                UserId = currentUserService.UserId
            };

            bool hasTempKey = false;

            switch (entry.State)
            {
                case EntityState.Added:
                    audit.Action = "Insert";
                    audit.NewValues = SerializeProperties(entry.CurrentValues);
                    hasTempKey = entry.Properties.Any(p => p.IsTemporary);
                    if (!hasTempKey)
                        audit.EntityId = GetPrimaryKeyValue(entry);
                    break;

                case EntityState.Modified:
                    audit.Action = "Update";
                    audit.EntityId = GetPrimaryKeyValue(entry);
                    audit.OldValues = SerializeChangedOldValues(entry);
                    audit.NewValues = SerializeChangedNewValues(entry);
                    audit.ChangedColumns = SerializeChangedColumns(entry);
                    break;

                case EntityState.Deleted:
                    audit.Action = "Delete";
                    audit.EntityId = GetPrimaryKeyValue(entry);
                    audit.OldValues = SerializeProperties(entry.OriginalValues);
                    break;
            }

            entries.Add((audit, entry, hasTempKey));

            if (!hasTempKey)
            {
                AuditEntries.Add(audit);
            }
        }

        return entries;
    }

    private async Task SavePendingAuditEntries(
        List<(AuditEntry Audit, EntityEntry Entry, bool HasTempKey)> pendingAudits,
        CancellationToken cancellationToken)
    {
        var hasDeferred = false;

        foreach (var (audit, entry, hasTempKey) in pendingAudits)
        {
            if (!hasTempKey) continue;

            audit.EntityId = GetPrimaryKeyValue(entry);
            audit.NewValues = SerializeProperties(entry.CurrentValues);
            AuditEntries.Add(audit);
            hasDeferred = true;
        }

        if (hasDeferred)
        {
            await base.SaveChangesAsync(cancellationToken);
        }
    }

    private static string GetPrimaryKeyValue(EntityEntry entry)
    {
        var keyProperties = entry.Metadata.FindPrimaryKey()?.Properties;
        if (keyProperties == null || keyProperties.Count == 0)
            return string.Empty;

        if (keyProperties.Count == 1)
            return entry.CurrentValues[keyProperties[0]]?.ToString() ?? string.Empty;

        var compositeKey = keyProperties
            .Select(p => entry.CurrentValues[p]?.ToString() ?? string.Empty);
        return string.Join(",", compositeKey);
    }

    private static string SerializeProperties(PropertyValues values)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in values.Properties)
        {
            dict[prop.Name] = values[prop];
        }
        return JsonSerializer.Serialize(dict);
    }

    private static string SerializeChangedOldValues(EntityEntry entry)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in entry.Properties.Where(p => p.IsModified))
        {
            dict[prop.Metadata.Name] = prop.OriginalValue;
        }
        return JsonSerializer.Serialize(dict);
    }

    private static string SerializeChangedNewValues(EntityEntry entry)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in entry.Properties.Where(p => p.IsModified))
        {
            dict[prop.Metadata.Name] = prop.CurrentValue;
        }
        return JsonSerializer.Serialize(dict);
    }

    private static string SerializeChangedColumns(EntityEntry entry)
    {
        var columns = entry.Properties
            .Where(p => p.IsModified)
            .Select(p => p.Metadata.Name)
            .ToList();
        return JsonSerializer.Serialize(columns);
    }
}
