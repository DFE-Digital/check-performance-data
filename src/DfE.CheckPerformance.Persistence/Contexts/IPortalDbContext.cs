using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Contexts;

public interface IPortalDbContext
{
    DbSet<AuditEntry> AuditEntries { get; }
    DbSet<CheckingWindow> CheckingWindows { get; }
    DbSet<ContentBlock> ContentBlocks { get; }
    DbSet<ContentBlockVersion> ContentBlockVersions { get; }
    DbSet<WikiPage> WikiPages { get; }
    DbSet<WikiPageVersion> WikiPageVersions { get; }
    
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}