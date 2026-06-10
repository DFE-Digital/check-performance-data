using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DfE.CheckPerformanceData.Persistence.Contexts;

public interface IPortalDbContext
{
    DatabaseFacade Database { get; }

    DbSet<ChangeRequest> ChangeRequests { get; }
    DbSet<AuditEntry> AuditEntries { get; }
    DbSet<CheckingWindow> CheckingWindows { get; }
    DbSet<ContentBlock> ContentBlocks { get; }
    DbSet<ContentBlockVersion> ContentBlockVersions { get; }
    DbSet<RulesConfigVersion> RulesConfigVersions { get; }
    DbSet<WikiPage> WikiPages { get; }
    DbSet<WikiPageVersion> WikiPageVersions { get; }
    DbSet<Pupil> Pupils { get; }
    DbSet<Setting> Settings { get; }
    DbSet<Country> Countries { get; }
    DbSet<QueueMessageEntity> QueueMessages { get; }
    DbSet<DeadLetterEntity> DeadLetters { get; }
    
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default);
}