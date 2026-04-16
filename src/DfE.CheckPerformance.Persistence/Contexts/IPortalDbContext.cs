using DfE.CheckPerformance.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformance.Persistence.Contexts;

public interface IPortalDbContext
{
    DbSet<Workflow> Workflows { get; }
    DbSet<WikiPage> WikiPages { get; }
    DbSet<WikiPageVersion> WikiPageVersions { get; }
    DbSet<ContentBlock> ContentBlocks { get; }
    DbSet<ContentBlockVersion> ContentBlockVersions { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}