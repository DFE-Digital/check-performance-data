using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Contexts;

public interface IPortalDbContext
{
    DbSet<ChangeRequest> ChangeRequests { get; }
    DbSet<AuditEntry> AuditEntries { get; }
    DbSet<CheckingWindow> CheckingWindows { get; }
    DbSet<ContentBlock> ContentBlocks { get; }
    DbSet<ContentBlockVersion> ContentBlockVersions { get; }
    DbSet<WikiPage> WikiPages { get; }
    DbSet<WikiPageVersion> WikiPageVersions { get; }
    DbSet<Pupil> Pupils { get; }
    DbSet<Country> Countries { get; }
    
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task ExecuteInTransactionAsync(Func<Task> work, CancellationToken cancellationToken = default);
}